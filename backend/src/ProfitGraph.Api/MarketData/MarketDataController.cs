using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.MarketData.Contracts;
using ProfitGraph.Api.MarketData.Converters;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.GetInstruments;
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

    /// <summary>
    ///     Tradable pairs of a venue whose symbol or assets contain <paramref name="query" />.
    /// </summary>
    [HttpGet("instruments")]
    public async Task<ActionResult<IReadOnlyList<InstrumentResponse>>> Instruments(
        [FromQuery] string? venue,
        CancellationToken cancellationToken,
        [FromQuery] string? query = "",
        [FromQuery] int limit = 20)
    {
        IReadOnlyList<InstrumentInternal> instruments = await sender.Send(
            new GetInstrumentsQuery(venue ?? string.Empty, query ?? string.Empty, limit),
            cancellationToken);

        return instruments.Select(instrument => instrument.ToResponse()).ToArray();
    }
}