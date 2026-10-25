using System.Text.Json;

using MediatR;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Application.Strategies;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Bots.CreateBot;

/// <summary>
///     Checks everything a later start needs: the latest strategy version compiles with the bot's values, the credential
///     belongs to the owner, the instrument exists on the credential's venue and its fee rates can be read.
/// </summary>
internal sealed class CreateBotCommandHandler(
    IStrategyRepository strategies,
    IExchangeCredentialRepository credentials,
    IBotRepository bots,
    ISecretProtector protector,
    VenueRegistry venues,
    VenuePorts<IInstrumentRulesSource> rules,
    VenuePorts<IVenueFeeRateReader> feeReaders,
    VenuePorts<IVenueBotSettings> venueSettings,
    BacktestCompiler compiler,
    ITransactionExecutor transactions,
    TimeProvider clock) : IRequestHandler<CreateBotCommand, BotInternal>
{
    public async Task<BotInternal> Handle(CreateBotCommand command, CancellationToken cancellationToken)
    {
        Strategy strategy = await strategies.GetOwned(command.StrategyId, command.OwnerId, cancellationToken);
        StrategyVersion version = await strategies.GetVersion(strategy.Id, strategy.CurrentVersion, cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");
        IReadOnlyDictionary<string, decimal> parameters = command.Parameters!;
        BotParameterRules.Ensure(version.Definition, parameters);
        compiler.Compile(version.Definition, parameters);
        ExchangeCredential credential = await credentials.GetOwned(command.CredentialId, command.OwnerId, cancellationToken);
        CandleSeries series = venues.ResolveSeries(credential.Venue.Value, command.Symbol, command.Timeframe);
        IVenueFeeRateReader reader = feeReaders.Find(series.Venue) ?? throw new VenueFeeRatesUnavailableException("This venue cannot run bots in this deployment.");
        IInstrumentRulesSource source = rules.Find(series.Venue) ?? throw new VenueFeeRatesUnavailableException("The venue has no instrument rules.");
        ExchangeCredentialSecretsInternal secrets = protector.Decrypt(credential);

        // A venue without anonymous market data reads the rules with the bot's own credential (ADR 0023, TC6).
        using (MarketDataAccess.Use(secrets))
        {
            await source.Get(series.Symbol, cancellationToken);
        }

        BotVenueSettings settings = Settings(series.Venue, command.VenueSettings);
        VenueFeeRatesInternal rates = await reader.Read(secrets, series.Symbol, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        var bot = Bot.Create(
            BotId.New(),
            new UserId(command.OwnerId),
            strategy.Id,
            version.Id,
            credential.Id,
            series,
            new BotParameters(parameters),
            command.AllocationCap,
            new BotRiskLimits(command.MaxOrderQuote, command.DailyLossLimitQuote),
            new BotFeeRates(rates.MakerRate, rates.TakerRate, now, rates.SellerFees),
            now,
            settings);

        if (command.AllowForeignOrders)
        {
            bot.ChangeForeignOrdersPolicy(allowForeignOrders: true, now);
        }

        await transactions.ExecuteAsync(
            async () =>
            {
                await bots.Add(bot, cancellationToken);

                return true;
            },
            cancellationToken);

        return bot.ToInternal();
    }

    /// <summary>
    ///     The venue module checks the request's settings; a venue without settings refuses any.
    /// </summary>
    private BotVenueSettings Settings(VenueId venue, JsonElement? requested)
    {
        IVenueBotSettings? schema = venueSettings.Find(venue);
        bool given = requested is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };

        if (schema is null)
        {
            return given
                ? throw new ApplicationValidationException([new InputError("invalid_value", "VenueSettings", "This venue has no bot settings.")])
                : BotVenueSettings.None;
        }

        return given ? schema.Read(requested!.Value) : schema.Defaults;
    }
}