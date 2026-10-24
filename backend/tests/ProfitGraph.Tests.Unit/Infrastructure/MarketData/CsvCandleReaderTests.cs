using System.Text;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Infrastructure.MarketData.Csv;

namespace ProfitGraph.Tests.Unit.Infrastructure.MarketData;

public sealed class CsvCandleReaderTests
{
    private const string Header = "openTime,open,high,low,close,volume\n";
    private const string Row = "2026-01-01T00:00:00Z,100,110,90,105,1.12345678\n";

    [TestCase(1001, 1000, 1_000_000, 1024)]
    [TestCase(2, 1000, 64, 1024)]
    [TestCase(1, 1000, 1_000_000, 40)]
    public async Task UsesConfiguredLimits(int rows, int maxRows, long maxFileSize, int maxRowLength)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Header + string.Concat(Enumerable.Repeat(Row, rows))));
        var options = new CsvImportOptions
        {
            BatchSize = 1,
            MaxRows = maxRows,
            MaxFileSizeBytes = maxFileSize,
            MaxRequestSizeBytes = maxFileSize + 1,
            MaxRowLength = maxRowLength
        };

        await Should.ThrowAsync<ApplicationValidationException>(async () => await new CsvCandleReader(options).Read(stream, CancellationToken.None).ToListAsync());
    }

    [Test]
    public async Task YieldsFirstCandleBeforeParsingInvalidLaterRow()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Header + "2026-01-01T00:00:00Z,100,110,90,105,1\ninvalid,row\n"));
        await using IAsyncEnumerator<CandleImportRowInternal> rows = Reader().Read(stream, CancellationToken.None).GetAsyncEnumerator();

        (await rows.MoveNextAsync()).ShouldBeTrue();
        rows.Current.LineNumber.ShouldBe(2);
        await Should.ThrowAsync<ApplicationValidationException>(async () => await rows.MoveNextAsync());
        stream.CanRead.ShouldBeTrue();
    }

    [Test]
    public async Task SupportsBomReorderedHeaderAndQuotedScalars_LeavesStreamOpen()
    {
        using var stream = new MemoryStream(
            [.. Encoding.UTF8.GetPreamble(), .. "volume,close,low,high,open,openTime\r\n\"1.25\",105,90,110,100,2026-01-01T00:00:00+00:00\r\n"u8]);

        List<CandleImportRowInternal> candles = await Reader().Read(stream, CancellationToken.None).ToListAsync();

        candles.Count.ShouldBe(1);
        candles[0].Candle.Volume.ShouldBe(1.25m);
        candles[0].Candle.Open.ShouldBe(100m);
        candles[0].Candle.OpenTime.Offset.ShouldBe(TimeSpan.Zero);
        candles[0].LineNumber.ShouldBe(2);
        stream.CanRead.ShouldBeTrue();
    }

    [TestCase("date,open,high,low,close,volume\n")]
    [TestCase("openTime,open,high,low,close,close\n")]
    [TestCase(Header + "2026-01-01T00:00:00Z,100,110,90,105,1e3\n")]
    [TestCase(Header + "2026-01-01T00:00:00Z,100,110,90,105,0.12345678901234567890123456789\n")]
    [TestCase(Header + "2026-01-01T00:00:00Z,100,110,90,105,\"1,000\"\n")]
    [TestCase(Header + "2026-01-01T03:00:00+03:00,100,110,90,105,1\n")]
    public async Task RejectsInvalidFormatAndLossyDecimals(string csv)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        await Should.ThrowAsync<ApplicationValidationException>(async () => await Reader().Read(stream, CancellationToken.None).ToListAsync());
    }

    [Test]
    public async Task RejectsInvalidUtf8()
    {
        using var stream = new MemoryStream([.. Encoding.UTF8.GetBytes(Header + "2026-01-01T00:00:00Z,100,110,90,105,"), 0xFF, 0x0A]);

        var error = await Should.ThrowAsync<ApplicationValidationException>(async () => await Reader().Read(stream, CancellationToken.None).ToListAsync());

        error.Errors.Single().Message.ShouldContain("UTF-8");
    }

    [Test]
    public async Task HonorsCancellation()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Header));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await Reader().Read(stream, cancellation.Token).ToListAsync(CancellationToken.None));
    }

    private static CsvCandleReader Reader()
    {
        return new CsvCandleReader(new CsvImportOptions());
    }
}