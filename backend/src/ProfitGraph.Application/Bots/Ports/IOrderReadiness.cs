using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     Tells the dispatcher whether a venue can take a submit now (ADR 0024), for example because the items to sell are
///     still under a trade hold. A venue without this port is always ready.
/// </summary>
public interface IOrderReadiness : IVenuePort
{
    /// <summary>
    ///     A pure read: it neither sends, reserves nor writes at the venue, and answers from the module's own state and
    ///     cache. An exception means the answer is not known and the submit waits.
    /// </summary>
    Task<OrderReadinessInternal> Evaluate(OrderSubmitInternal submit, CancellationToken cancellationToken);
}