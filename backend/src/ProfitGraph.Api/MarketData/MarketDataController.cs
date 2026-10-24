using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.MarketData.Contracts;
using ProfitGraph.Api.MarketData.Converters;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.GetVenues;

namespace ProfitGraph.Api.MarketData;

[ApiController]
[Route("api/market-data")]
public sealed class MarketDataController(ISender sender) : ControllerBase
{
    [HttpGet("venues")]
    public async Task<ActionResult<IReadOnlyList<VenueResponse>>> Venues(CancellationToken cancellationToken)
    {
        IReadOnlyList<VenueInternal> venues = await sender.Send(new GetVenuesQuery(), cancellationToken);

        return venues.Select(venue => venue.ToResponse()).ToArray();
    }
}