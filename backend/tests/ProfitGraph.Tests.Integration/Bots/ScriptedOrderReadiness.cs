using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     A venue's readiness the test controls: ready until it is told to hold submits for a reason.
/// </summary>
internal sealed class ScriptedOrderReadiness : IOrderReadiness
{
    /// <summary>
    ///     When set, every submit waits for this reason.
    /// </summary>
    public OrderWaitingCode? Hold { get; set; }

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public Task<OrderReadinessInternal> Evaluate(OrderSubmitInternal submit, CancellationToken cancellationToken)
    {
        return Task.FromResult(Hold is { } code ? new OrderReadinessInternal(code, RecheckAt: DateTimeOffset.UtcNow.AddSeconds(5)) : OrderReadinessInternal.Ready);
    }
}