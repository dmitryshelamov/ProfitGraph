using MediatR;

using ProfitGraph.Application.Bots.CancelBotOrder;
using ProfitGraph.Application.Bots.StopBot;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Application.Scanner.TrackScanTrade;

internal sealed class TrackScanTradeCommandHandler(
    IScanTradeRepository trades,
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    ITransactionExecutor transactions,
    ISender sender,
    TimeProvider clock) : IRequestHandler<TrackScanTradeCommand, bool>
{
    /// <summary>
    ///     A trade's bot starts right after the trade is stored; until then a stopped bot without a run is not the owner's stop.
    /// </summary>
    private static readonly TimeSpan StartGrace = TimeSpan.FromMinutes(1);

    public async Task<bool> Handle(TrackScanTradeCommand command, CancellationToken cancellationToken)
    {
        ScanTrade? trade = await trades.Get(new ScanTradeId(command.TradeId), cancellationToken);
        Bot? bot = trade is null ? null : await bots.Get(trade.BotId, cancellationToken);

        if (trade is null || bot is null || trade is { IsLive: false, BotReleased: true } || trade.Status == ScanTradeStatus.Open)
        {
            return false;
        }

        ExternalAccountCheckpoint? account = (await checkpoints.Get(bot.Id, cancellationToken))?.Engine.Account;
        OrderSnapshot[] working = account is null
            ? []
            : [.. account.Orders.Where(order => order.Status is OrderStatus.Active or OrderStatus.PendingSubmit or OrderStatus.PendingCancel)];
        DateTimeOffset now = clock.GetUtcNow();

        if (!trade.IsLive)
        {
            return await Release(trade, bot, working, cancellationToken);
        }

        if (working.FirstOrDefault(order => order.Tag == LevelTradeStrategyBlockFactory.EntryTag) is { } entry)
        {
            return trade.IsOverdue(now) && entry.Status != OrderStatus.PendingCancel && await CancelEntry(trade, entry, cancellationToken);
        }

        bool ran = account is not null;
        bool stoppedByOwner = bot.DesiredState == BotDesiredState.Stopped && (ran || now - trade.CreatedAt >= StartGrace);

        if (working.Length > 0 || account is { BaseTotal: not 0m } || (!ran && !stoppedByOwner))
        {
            return false;
        }

        bool ended = await transactions.ExecuteAsync(
            async () =>
            {
                ScanTrade? stored = await trades.GetForUpdate(trade.Id, cancellationToken);
                bool changed = stored is not null &&
                    (stoppedByOwner ? stored.Cancel(now) : stored.IsOverdue(now) ? stored.Expire(now) : stored.Fail(ScanTradeErrorCodes.EntryEnded, now));

                if (changed)
                {
                    await trades.Update(stored!, cancellationToken);
                }

                return changed;
            },
            cancellationToken);

        return ended;
    }

    private async Task<bool> CancelEntry(ScanTrade trade, OrderSnapshot entry, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new CancelBotOrderCommand(trade.OwnerId.Value, trade.BotId.Value, entry.Id), cancellationToken);

            return true;
        }
        catch (StateConflictException)
        {
            // A cancel already waits, or the order stopped working meanwhile; the next round sees which.
            return false;
        }
    }

    private async Task<bool> Release(ScanTrade trade, Bot bot, OrderSnapshot[] working, CancellationToken cancellationToken)
    {
        if (bot.DesiredState == BotDesiredState.Running)
        {
            if (working.Length > 0)
            {
                return false;
            }

            await sender.Send(new StopBotCommand(trade.OwnerId.Value, bot.Id.Value), cancellationToken);
        }

        return await transactions.ExecuteAsync(
            async () =>
            {
                ScanTrade? stored = await trades.GetForUpdate(trade.Id, cancellationToken);

                if (stored is null || !stored.ReleaseBot())
                {
                    return false;
                }

                await trades.Update(stored, cancellationToken);

                return true;
            },
            cancellationToken);
    }
}