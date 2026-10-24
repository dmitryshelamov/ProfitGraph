using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Infrastructure.MarketData.Csv.Converters;

/// <summary>
///     Reads a decimal written with a dot and rejects any value that decimal would have to round.
/// </summary>
internal sealed class ExactDecimalCsvConverter : DefaultTypeConverter
{
    public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
    {
        if (text is not null &&
            decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) &&
            Normalize(text) == Normalize(value.ToString(CultureInfo.InvariantCulture)))
        {
            return value;
        }

        string column = row.HeaderRecord![row.CurrentIndex];

        throw new ApplicationValidationException(
        [
            new InputError(
                "invalid_csv",
                $"File.Line[{row.Parser.RawRow}].{column}",
                "Value must be an exact decimal using a dot, without grouping or exponent notation.")
        ]);
    }

    private static string Normalize(string value)
    {
        bool negative = value.StartsWith('-');
        string[] parts = value.TrimStart('+', '-').Split('.');
        string integer = parts[0].TrimStart('0');
        string fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        string normalized = (integer.Length == 0 ? "0" : integer) + (fraction.Length == 0 ? string.Empty : "." + fraction);

        return negative && normalized != "0" ? "-" + normalized : normalized;
    }
}