using MediatR;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Positions;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Templates;

using ScanTradeExit = ProfitGraph.Engine.Scanning.ScanTradeExit;

namespace ProfitGraph.Application.Scanner.Trades;

/// <summary>
///     Follows a scanner trade's bot as the outbox delivers its events (ADR 0060): the opened position opens the trade, a filled exit
///     at the target or the stop names the exit, a sale for a long and a buy for a short (ADR 0061), and the closed position closes the
///     trade with the bot's realized profit. Events of other bots are ignored.
/// </summary>
internal sealed class ScanTradeBotEventsHandler(IScanTradeRepository trades)
    : INotificationHandler<BotPositionOpened>,
        INotificationHandler<BotPositionClosed>,
        INotificationHandler<BotOrderFilled>
{
    public Task Handle(BotOrderFilled notification, CancellationToken cancellationToken)
    {
        ScanTradeExit? exit = notification.Fill switch
        {
            { Tag: LevelTradeStrategyBlockFactory.TargetTag } => ScanTradeExit.Target,
            { Tag: LevelTradeStrategyBlockFactory.StopTag } => ScanTradeExit.Stop,
            _ => null
        };

        return exit is { } value ? Change(notification.BotId, trade => trade.RecordExit(value), cancellationToken) : Task.CompletedTask;
    }

    public Task Handle(BotPositionClosed notification, CancellationToken cancellationToken)
    {
        return Change(notification.BotId, trade => trade.Close(notification.RealizedPnl, notification.At), cancellationToken);
    }

    public Task Handle(BotPositionOpened notification, CancellationToken cancellationToken)
    {
        return Change(notification.BotId, trade => trade.Open(notification.At), cancellationToken);
    }

    private async Task Change(BotId botId, Func<ScanTrade, bool> change, CancellationToken cancellationToken)
    {
        ScanTrade? trade = await trades.GetByBotForUpdate(botId, cancellationToken);

        if (trade is not null && change(trade))
        {
            await trades.Update(trade, cancellationToken);
        }
    }
}