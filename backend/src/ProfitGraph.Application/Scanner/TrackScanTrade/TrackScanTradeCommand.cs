using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Scanner.TrackScanTrade;

/// <summary>
///     The Worker's look at one scanner trade (ADR 0060). A waiting trade whose entry is overdue asks the bot to cancel it. A waiting trade
///     whose bot ran and holds neither an order nor a position ends: expired when overdue, cancelled when the owner stopped the bot,
///     failed otherwise (the venue rejected the entry). A finished trade stops its bot once the bot has no working order, and is released.
///     Returns whether anything was asked or changed. It sends commands that reach the venue, so it runs outside a transaction.
/// </summary>
public sealed record TrackScanTradeCommand(Guid TradeId) : ICommand<bool>, INonTransactionalCommand;