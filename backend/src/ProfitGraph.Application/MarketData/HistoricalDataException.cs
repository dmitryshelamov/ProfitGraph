using System.Globalization;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData;

/// <summary>
///     A history problem with the candle ranges it concerns; the ranges are part of the message people see.
/// </summary>
public sealed class HistoricalDataException(string code, string message, IReadOnlyList<CandleRangeInternal> ranges)
    : ApplicationExceptionBase(code, Describe(message, ranges))
{
    public IReadOnlyList<CandleRangeInternal> Ranges { get; } = ranges;

    private static string Describe(string message, IReadOnlyList<CandleRangeInternal> ranges)
    {
        return ranges.Count == 0
            ? message
            : $"{message} Ranges: {string.Join(", ", ranges.Select(range => string.Create(CultureInfo.InvariantCulture, $"[{range.From:O}, {range.To:O})")))}.";
    }
}