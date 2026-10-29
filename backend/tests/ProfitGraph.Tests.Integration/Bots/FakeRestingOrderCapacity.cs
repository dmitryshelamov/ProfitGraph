using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The test venue keeps up to <see cref="Limit" /> resting orders per bot, so grid bots run on it (ADR 0058).
/// </summary>
public sealed class FakeRestingOrderCapacity : IVenueRestingOrderCapacity
{
    public const int DefaultLimit = 50;

    public int Limit { get; set; } = DefaultLimit;

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public int MaxRestingOrders(InstrumentSymbol symbol)
    {
        return Limit;
    }
}