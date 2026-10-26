using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Backtesting.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.MarketData.Contracts;
using ProfitGraph.Api.MarketData.Converters;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.GetInstrumentRules;
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
        [FromQuery] int limit = 20,
        [FromQuery] Guid? credentialId = null)
    {
        Guid userId = AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
        IReadOnlyList<InstrumentInternal> instruments = await sender.Send(
            new GetInstrumentsQuery(venue ?? string.Empty, query ?? string.Empty, limit, userId, credentialId),
            cancellationToken);

        return instruments.Select(instrument => instrument.ToResponse()).ToArray();
    }

    /// <summary>
    ///     The venue's current rules of one instrument, read now and not stored; a backtest captures its own when it runs.
    /// </summary>
    [HttpGet("instrument-rules")]
    public async Task<ActionResult<InstrumentRulesResponse>> InstrumentRules(
        [FromQuery] string? venue,
        [FromQuery] string? symbol,
        CancellationToken cancellationToken,
        [FromQuery] Guid? credentialId = null)
    {
        Guid userId = AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
        InstrumentRulesDefinitionInternal definition = await sender.Send(
            new GetInstrumentRulesQuery(venue ?? string.Empty, symbol ?? string.Empty, userId, credentialId),
            cancellationToken);

        return definition.Rules.ToResponse();
    }
}