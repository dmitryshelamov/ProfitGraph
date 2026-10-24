using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.GetVenues;

public sealed record GetVenuesQuery : IQuery<IReadOnlyList<VenueInternal>>;