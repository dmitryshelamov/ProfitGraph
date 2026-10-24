using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.MarketData.Contracts;
using ProfitGraph.Api.MarketData.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.GetCandles;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Api.MarketData;

[ApiController]
[Route("api/historical-data")]
public sealed class HistoricalDataController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     Streams an uploaded CSV into the shared candle cache; only the Owner may change it (ADR 0010).
    /// </summary>
    [HttpPost("csv")]
    [Authorize(Roles = AuthenticationSetup.OwnerRole)]
    [Consumes("multipart/form-data")]
    [ServiceFilter(typeof(CsvImportLimitsFilter), Order = 900)]
    public async Task<ActionResult<CandleImportResponse>> Import(
        [FromForm] ImportCandlesRequest request,
        [FromServices] ICsvCandleReader csv,
        [FromServices] CsvImportOptions options,
        CancellationToken cancellationToken)
    {
        if (request.File.Length <= 0 || request.File.Length > options.MaxFileSizeBytes)
        {
            throw new ApplicationValidationException([new InputError("invalid_csv", "File", $"CSV must contain data and be at most {options.MaxFileSizeBytes} bytes.")]);
        }

        await using Stream stream = request.File.OpenReadStream();
        CandleImportInternal result = await sender.Send(
            new ImportCandlesCommand(
                request.Venue ?? string.Empty,
                request.Symbol ?? string.Empty,
                request.Timeframe ?? string.Empty,
                csv.Read(stream, cancellationToken)),
            cancellationToken);

        return result.ToResponse();
    }

    [HttpGet("candles")]
    public async Task<ActionResult<IReadOnlyList<CandleResponse>>> Candles(
        [FromQuery] string? venue,
        [FromQuery] string? symbol,
        [FromQuery] string? timeframe,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 1000)
    {
        IReadOnlyList<CandleInternal> candles = await sender.Send(
            new GetCandlesQuery(venue ?? string.Empty, symbol ?? string.Empty, timeframe ?? string.Empty, from, to, limit),
            cancellationToken);

        return candles.Select(candle => candle.ToResponse()).ToArray();
    }
}