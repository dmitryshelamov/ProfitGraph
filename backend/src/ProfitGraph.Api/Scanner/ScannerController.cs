using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Api.Scanner.Converters;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.CreateScanPreset;
using ProfitGraph.Application.Scanner.DeleteScanPreset;
using ProfitGraph.Application.Scanner.GetScanCandles;
using ProfitGraph.Application.Scanner.GetScanPresets;
using ProfitGraph.Application.Scanner.GetScanSnapshot;
using ProfitGraph.Application.Scanner.GetScanVenues;
using ProfitGraph.Application.Scanner.UpdateScanPreset;

namespace ProfitGraph.Api.Scanner;

/// <summary>
///     The market scanner (ADR 0059): the venues that have one, a venue's current snapshot, and the built-in and the user's own
///     presets. Filtering and sorting happen on the page; the snapshot is shared by every user.
/// </summary>
[ApiController]
[Route("api/scanner")]
public sealed class ScannerController(ISender sender) : ControllerBase
{
    [HttpGet("venues")]
    public async Task<ActionResult<IReadOnlyList<ScanVenueResponse>>> GetVenues(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScanVenueInternal> venues = await sender.Send(new GetScanVenuesQuery(), cancellationToken);

        return Ok(venues.Select(venue => new ScanVenueResponse(venue.Venue, venue.Name)).ToList());
    }

    [HttpGet("{venue}/snapshot")]
    public async Task<ActionResult<ScanSnapshotResponse>> GetSnapshot(string venue, CancellationToken cancellationToken)
    {
        ScanSnapshotInternal snapshot = await sender.Send(new GetScanSnapshotQuery(venue), cancellationToken);

        return snapshot.ToResponse();
    }

    /// <summary>
    ///     A page of a pair's chart: its closed candles of the half-open UTC range [from, to) on the timeframe, at most 1000 of
    ///     them, read through the shared candle cache from the venue's public history.
    /// </summary>
    [HttpGet("{venue}/candles")]
    public async Task<ActionResult<ScanCandlesResponse>> GetCandles(
        string venue,
        [FromQuery] string? symbol,
        [FromQuery] string? timeframe,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        ScanCandlesInternal page = await sender.Send(
            new GetScanCandlesQuery(venue, symbol ?? string.Empty, timeframe ?? string.Empty, from, to),
            cancellationToken);

        return page.ToResponse();
    }

    [HttpGet("{venue}/presets")]
    public async Task<ActionResult<IReadOnlyList<ScanPresetResponse>>> GetPresets(string venue, CancellationToken cancellationToken)
    {
        IReadOnlyList<ScanPresetInternal> presets = await sender.Send(new GetScanPresetsQuery(UserId(), venue), cancellationToken);

        return Ok(presets.Select(preset => preset.ToResponse()).ToList());
    }

    [HttpPost("{venue}/presets")]
    public async Task<ActionResult<ScanPresetResponse>> CreatePreset(string venue, ScanPresetRequest request, CancellationToken cancellationToken)
    {
        ScanPresetInternal preset = await sender.Send(new CreateScanPresetCommand(UserId(), venue, request.ToInternal()), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, preset.ToResponse());
    }

    [HttpPut("presets/{presetId:guid}")]
    public async Task<ActionResult<ScanPresetResponse>> UpdatePreset(Guid presetId, ScanPresetRequest request, CancellationToken cancellationToken)
    {
        ScanPresetInternal preset = await sender.Send(new UpdateScanPresetCommand(UserId(), presetId, request.ToInternal()), cancellationToken);

        return preset.ToResponse();
    }

    [HttpDelete("presets/{presetId:guid}")]
    public async Task<IActionResult> DeletePreset(Guid presetId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteScanPresetCommand(UserId(), presetId), cancellationToken);

        return NoContent();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}