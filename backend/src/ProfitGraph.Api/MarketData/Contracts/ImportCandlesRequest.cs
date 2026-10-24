using System.ComponentModel.DataAnnotations;

namespace ProfitGraph.Api.MarketData.Contracts;

/// <summary>
///     The CSV upload form: one series and one UTF-8 file. The series is checked by the import command, which reports
///     an unknown venue, symbol or timeframe with its own error code.
/// </summary>
public sealed class ImportCandlesRequest
{
    public string? Venue { get; init; }

    public string? Symbol { get; init; }

    public string? Timeframe { get; init; }

    [Required]
    public IFormFile File { get; init; } = null!;
}