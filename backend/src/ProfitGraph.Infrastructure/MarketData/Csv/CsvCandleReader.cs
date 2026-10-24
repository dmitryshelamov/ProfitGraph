using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

using CsvHelper;
using CsvHelper.Configuration;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Engine.Market;
using ProfitGraph.Infrastructure.MarketData.Csv.Converters;

namespace ProfitGraph.Infrastructure.MarketData.Csv;

/// <summary>
///     Streams candles out of a UTF-8 CSV with CsvHelper, keeping line numbers and enforcing the configured size limits.
/// </summary>
internal sealed class CsvCandleReader(CsvImportOptions options) : ICsvCandleReader
{
    /// <summary>
    ///     A UTF-8 byte order mark that some spreadsheet exports put before the header.
    /// </summary>
    private const char ByteOrderMark = (char)0xFEFF;

    private static readonly string[] Columns = ["opentime", "open", "high", "low", "close", "volume"];

    public async IAsyncEnumerable<CandleImportRowInternal> Read(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        using var csv = new CsvReader(
            reader,
            new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ",",
                PrepareHeaderForMatch = args => args.Header.TrimStart(ByteOrderMark).Trim().ToLowerInvariant(),
                TrimOptions = TrimOptions.Trim,
                DetectColumnCountChanges = true,
                LineBreakInQuotedFieldIsBadData = true,
                MaxFieldSize = options.MaxRowLength,
                CountBytes = true,
                Encoding = Encoding.UTF8,
                ExceptionMessagesContainRawData = false
            });
        csv.Context.TypeConverterCache.AddConverter<DateTimeOffset>(new UtcTimestampCsvConverter());
        csv.Context.TypeConverterCache.AddConverter<decimal>(new ExactDecimalCsvConverter());
        var rowCount = 0;

        while (await ReadNext(csv, cancellationToken))
        {
            if (++rowCount > options.MaxRows)
            {
                throw Invalid(csv, $"CSV must contain at most {options.MaxRows} candles.");
            }

            yield return ReadCandle(csv);
        }

        if (rowCount == 0)
        {
            throw Invalid(csv, "CSV must contain at least one candle.");
        }
    }

    private async Task<bool> ReadNext(CsvReader csv, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (csv.HeaderRecord is null)
            {
                if (!await csv.ReadAsync())
                {
                    throw Invalid(csv, "CSV must contain a header and at least one candle.");
                }

                csv.ReadHeader();
                ValidateSize(csv);
                string[] header = [.. csv.HeaderRecord!.Select(value => value.TrimStart(ByteOrderMark).Trim().ToLowerInvariant())];

                if (header.Length != Columns.Length || !header.ToHashSet().SetEquals(Columns))
                {
                    throw Invalid(csv, "Expected header: openTime,open,high,low,close,volume.");
                }
            }

            bool available = await csv.ReadAsync();
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSize(csv);

            return available;
        }
        catch (CsvHelperException)
        {
            throw Invalid(csv, "Malformed CSV: check columns, quoting and field length.");
        }
        catch (DecoderFallbackException)
        {
            throw Invalid(csv, "CSV must use valid UTF-8.");
        }
    }

    private static CandleImportRowInternal ReadCandle(CsvReader csv)
    {
        try
        {
            var candle = new Candle(
                csv.GetField<DateTimeOffset>("opentime"),
                csv.GetField<decimal>("open"),
                csv.GetField<decimal>("high"),
                csv.GetField<decimal>("low"),
                csv.GetField<decimal>("close"),
                csv.GetField<decimal>("volume"));

            return new CandleImportRowInternal(candle, csv.Parser.RawRow);
        }
        catch (CsvHelperException)
        {
            throw Invalid(csv, "Malformed CSV: check columns, quoting and field length.");
        }
    }

    private void ValidateSize(CsvReader csv)
    {
        if (csv.Parser.ByteCount > options.MaxFileSizeBytes || csv.Parser.RawRecord.TrimEnd('\r', '\n').Length > options.MaxRowLength)
        {
            throw Invalid(csv, $"CSV exceeds the {options.MaxFileSizeBytes}-byte file or {options.MaxRowLength}-character row limit.");
        }
    }

    private static ApplicationValidationException Invalid(CsvReader csv, string message)
    {
        return new ApplicationValidationException([new InputError("invalid_csv", $"File.Line[{csv.Parser.RawRow}]", message)]);
    }
}