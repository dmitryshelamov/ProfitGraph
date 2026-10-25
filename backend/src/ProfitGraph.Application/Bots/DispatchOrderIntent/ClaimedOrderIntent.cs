using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Bots.DispatchOrderIntent;

/// <summary>
///     An intent this dispatcher claimed, with the venue and per-order limit of its bot and, for a submit, the lease epoch
///     it was claimed under.
/// </summary>
internal sealed record ClaimedOrderIntent(OrderIntent Intent, VenueId Venue, decimal MaxOrderQuote, long? Epoch);