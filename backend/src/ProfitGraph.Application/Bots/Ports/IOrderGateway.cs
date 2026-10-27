using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     A venue's order execution for live bots (ADR 0016). It exists only in a Worker with the <c>live</c> role and only
///     when trading is enabled; without it a bot degrades with <c>NoGateway</c> and sends nothing. The gateway owns the
///     format of the client order identifiers the venue knows orders by.
/// </summary>
public interface IOrderGateway : IVenuePort
{
    /// <summary>
    ///     The deterministic venue identifier of an engine order of a bot.
    /// </summary>
    ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId);

    /// <summary>
    ///     The bot's order updates as the venue reports them, in an order the engine accepts.
    /// </summary>
    IAsyncEnumerable<ExternalOrderUpdate> Updates(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     Places a submit. The gateway calls <paramref name="markPlaceStarted" /> before the place request may leave the
    ///     process; a delivery that must look up first never places again unless the venue proves the order absent.
    ///     Returns a definitive rejection, or null when the venue accepted the order (its updates follow). Any other
    ///     outcome is an exception, and the intent is retried.
    /// </summary>
    Task<ExternalOrderUpdate?> Submit(OrderSubmitInternal submit, Func<CancellationToken, Task> markPlaceStarted, CancellationToken cancellationToken);

    /// <summary>
    ///     Requests cancellation of an order by its client order identifier; the outcome follows as an update.
    /// </summary>
    Task Cancel(BotId botId, ClientOrderId clientOrderId, CancellationToken cancellationToken);

    /// <summary>
    ///     Establishes the bot's venue state before its submits may go: looks up each order, checks what else trades on the
    ///     bot's symbol and whether its key may trade. Returns the updates of the orders it found and, when the bot must not
    ///     trade, a safety reason. A venue that cannot answer now throws, and the runner tries again later.
    /// </summary>
    Task<BotReconciliationInternal> Reconcile(BotReconcileInternal request, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether the gateway would close, on its own evidence and without the owner, every order of the request that the
    ///     venue cannot confirm (ADR 0030). Only reads: nothing is sent to the venue, the module's own records stay as they
    ///     are, and no stream is opened. False for a gateway that does not prove absences, and when any check fails or cannot
    ///     be read now.
    /// </summary>
    Task<bool> CanProveAbsence(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}