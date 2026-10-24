using MediatR;

using ProfitGraph.Application.Accounts;
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
        await (rules.Find(series.Venue) ?? throw new VenueFeeRatesUnavailableException("The venue has no instrument rules.")).Get(series.Symbol, cancellationToken);
        VenueFeeRatesInternal rates = await reader.Read(protector.Decrypt(credential), series.Symbol, cancellationToken);
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
            new BotFeeRates(rates.MakerRate, rates.TakerRate, now),
            now);

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
}