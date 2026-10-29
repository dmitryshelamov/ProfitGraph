using System.Globalization;
using System.Text.Json;

using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.CreateBot;
using ProfitGraph.Application.Bots.RenameBot;
using ProfitGraph.Application.Bots.StartBot;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Strategies.Templates;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Engine.Templates;

using ScanTrade = ProfitGraph.Domain.Scanner.Trades.ScanTrade;

namespace ProfitGraph.Application.Scanner.CreateScanTrade;

/// <summary>
///     Refuses a trade without settings, on a pair the snapshot does not show, with an entry further than
///     <see cref="ScanTradeOptions.MaxEntryDeviation" /> from the current price, or while the owner has a live trade with the same
///     scenario and prices on the pair. The bot's allocation, largest order and daily loss limit are the trade's amount, so it cannot
///     spend more; it allows foreign orders, since the owner's other trades on the pair place theirs on the same symbol. A bot that
///     does not start fails the trade and the error reaches the owner. On a margin venue (ADR 0061) the bot trades at the settings'
///     leverage, so its allocation is the trade's margin and fees rather than its amount; a short trade runs the short strategy.
/// </summary>
internal sealed class CreateScanTradeCommandHandler(
    IEnumerable<IMarketScanSource> sources,
    VenueRegistry venues,
    IScanTradeSettingsRepository settings,
    IScanTradeRepository trades,
    IScanSnapshotRepository snapshots,
    IStrategyRepository strategies,
    ITransactionExecutor transactions,
    ISender sender,
    ScanTradeOptions options,
    TimeProvider clock) : IRequestHandler<CreateScanTradeCommand, ScanTradeInternal>
{
    private const string StrategyName = "Сделка по уровням (сканер)";
    private const string ShortStrategyName = "Сделка по уровням, шорт (сканер)";
    private const int AmountDecimals = 2;

    public async Task<ScanTradeInternal> Handle(CreateScanTradeCommand command, CancellationToken cancellationToken)
    {
        sources.Find(command.Venue);
        var owner = new UserId(command.OwnerId);
        var venue = new VenueId(command.Venue);
        ScanTradeSettings sizing = await settings.Get(owner, venue, cancellationToken) ??
            throw new StateConflictException("Set the risk and the credential of trades first.", ScanTradeErrorCodes.NoSettings);
        bool @short = command.Direction == PositionDirection.Short;

        if (@short && venues.Find(command.Venue) is not { SupportsShort: true })
        {
            throw new StateConflictException("This venue has no short trades.", ScanTradeErrorCodes.ShortUnsupported);
        }

        await EnsureEntryNearPrice(command, cancellationToken);

        IReadOnlyList<ScanTrade> pairTrades = await trades.List(owner, venue, command.Symbol, options.ListLimit, cancellationToken);

        if (pairTrades.Any(trade => trade.IsLive && trade.Takes(command.Scenario, command.Entry, command.Stop, command.Target)))
        {
            throw new StateConflictException("A live trade already has this scenario and these prices.", ScanTradeErrorCodes.LiveTradeExists);
        }

        (decimal amount, decimal risk) = sizing.Size(command.Entry, command.Stop, options.FeeRate);
        amount = decimal.Round(amount, AmountDecimals, MidpointRounding.ToZero);
        StrategyId strategy = await LevelTradeStrategy(sizing, @short, cancellationToken);
        decimal allocation = sizing.Leverage is { } leverage
            ? decimal.Round(amount / leverage + 2 * amount * options.FeeRate, AmountDecimals, MidpointRounding.AwayFromZero)
            : amount;
        BotInternal bot = await sender.Send(
            new CreateBotCommand(
                command.OwnerId,
                strategy.Value,
                sizing.CredentialId.Value,
                command.Symbol,
                options.Timeframe,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    [@short ? "sellAmount" : "buyAmount"] = amount,
                    ["entryPrice"] = command.Entry,
                    ["stopPrice"] = command.Stop,
                    ["targetPrice"] = command.Target
                },
                allocation,
                amount,
                amount,
                AllowForeignOrders: true,
                sizing.Leverage is { } botLeverage
                    ? JsonSerializer.SerializeToElement(new { leverage = botLeverage.ToString(CultureInfo.InvariantCulture) })
                    : null,
                command.Venue),
            cancellationToken);
        await sender.Send(new RenameBotCommand(command.OwnerId, bot.Id, Name(command)), cancellationToken);
        var trade = ScanTrade.Create(
            ScanTradeId.New(),
            owner,
            venue,
            command.Symbol,
            command.Scenario,
            Timeframe.Parse(command.Timeframe),
            command.Entry,
            command.Stop,
            command.Target,
            amount,
            risk,
            new BotId(bot.Id),
            command.Scenario == ScanScenario.Pullback ? ScanTradeIdeas.PullbackWait : ScanTradeIdeas.MarketEntryWait,
            clock.GetUtcNow(),
            command.Direction);
        await transactions.ExecuteAsync(
            async () =>
            {
                await trades.Add(trade, cancellationToken);

                return true;
            },
            cancellationToken);

        try
        {
            await sender.Send(new StartBotCommand(command.OwnerId, bot.Id), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await transactions.ExecuteAsync(
                async () =>
                {
                    ScanTrade? stored = await trades.GetForUpdate(trade.Id, cancellationToken);

                    if (stored is not null && stored.Fail(ScanTradeErrorCodes.StartFailed, clock.GetUtcNow()))
                    {
                        await trades.Update(stored, cancellationToken);
                    }

                    return true;
                },
                cancellationToken);

            throw;
        }

        return trade.ToInternal();
    }

    private static string Name(CreateScanTradeCommand command)
    {
        string scenario = command.Scenario switch
        {
            ScanScenario.Levels or ScanScenario.LevelsRewardRisk2 => $"уровни {command.Timeframe}",
            ScanScenario.Target2R => $"цель 2R от поддержки {command.Timeframe}",
            _ => "откат"
        };

        return command.Direction == PositionDirection.Short
            ? string.Create(CultureInfo.InvariantCulture, $"{command.Symbol} · шорт · {scenario}")
            : string.Create(CultureInfo.InvariantCulture, $"{command.Symbol} · {scenario}");
    }

    private async Task EnsureEntryNearPrice(CreateScanTradeCommand command, CancellationToken cancellationToken)
    {
        ScanSnapshotInternal? snapshot = await snapshots.Get(command.Venue, cancellationToken);

        if (snapshot?.Rows.FirstOrDefault(row => row.Symbol == command.Symbol)?.Metrics.Price is not { } price)
        {
            throw new StateConflictException("The scanner does not show this pair now.", ScanTradeErrorCodes.PairNotScanned);
        }

        if (Math.Abs(command.Entry - price) > price * options.MaxEntryDeviation)
        {
            throw new StateConflictException("The price has moved away from the entry; open the cards again.", ScanTradeErrorCodes.EntryMoved);
        }
    }

    /// <summary>
    ///     The owner's level trade strategy, long or short, created from its template with the first trade of its direction; every
    ///     trade's bot runs its latest version.
    /// </summary>
    private async Task<StrategyId> LevelTradeStrategy(ScanTradeSettings sizing, bool @short, CancellationToken cancellationToken)
    {
        if ((@short ? sizing.ShortStrategyId : sizing.StrategyId) is { } existing && await strategies.Get(existing, cancellationToken) is not null)
        {
            return existing;
        }

        return await transactions.ExecuteAsync(
            async () =>
            {
                DateTimeOffset now = clock.GetUtcNow();
                (Strategy strategy, StrategyVersion version) = Strategy.Create(
                    StrategyId.New(),
                    sizing.OwnerId,
                    new StrategyName(@short ? ShortStrategyName : StrategyName),
                    @short ? LevelTradeShortStrategyBlockFactory.Create() : LevelTradeStrategyBlockFactory.Create(),
                    StrategyTemplateLayouts.LevelTrade(),
                    now);
                await strategies.Add(strategy, version, cancellationToken);

                if (@short)
                {
                    sizing.UseShortStrategy(strategy.Id, now);
                }
                else
                {
                    sizing.UseStrategy(strategy.Id, now);
                }

                await settings.Save(sizing, cancellationToken);

                return strategy.Id;
            },
            cancellationToken);
    }
}