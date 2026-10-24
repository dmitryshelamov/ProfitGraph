using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Infrastructure.MarketData.Csv.Converters;

/// <summary>
///     Accepts only explicit UTC ISO-8601 times, so the server's local time zone never changes a candle.
/// </summary>
internal sealed class UtcTimestampCsvConverter : DefaultTypeConverter
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];

    public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
    {
        if (text is not null &&
            (text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal)) &&
            DateTimeOffset.TryParseExact(
                text,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset value))
        {
            return value;
        }

        throw new ApplicationValidationException(
        [
            new InputError("invalid_csv", $"File.Line[{row.Parser.RawRow}].openTime", "openTime must be an ISO-8601 UTC timestamp with Z or +00:00.")
        ]);
    }
}