using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.MarketData;

[NonParallelizable]
public sealed class HistoricalDataEndpointTests
{
    private const string Header = "openTime,open,high,low,close,volume\n";
    private const string Original = "2026-01-01T01:00:00Z,100,110,90,105,1.1234567890123456789\n";
    private string _symbol = string.Empty;

    /// <summary>
    ///     Candles are a cache shared by every user, so each test works on its own symbol.
    /// </summary>
    [SetUp]
    public void ChooseSymbol()
    {
        _symbol = "T" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    [Test]
    public async Task ImportsAndReadsExactDecimalValues_Idempotently()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage first = await Import(client, Header + Original + Original);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await first.Content.ReadFromJsonAsync<JsonNode>())!;
        result["accepted"]!.GetValue<int>().ShouldBe(1);
        result["symbol"]!.GetValue<string>().ShouldBe(_symbol);
        result["venue"]!.GetValue<string>().ShouldBe("bybit");

        using HttpResponseMessage repeated = await Import(client, Header + Original);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonArray candles = await Read(client);
        candles.Count.ShouldBe(1);
        candles[0]!["volume"]!.GetValue<string>().ShouldBe("1.1234567890123456789");
    }

    [Test]
    public async Task ImportReplacesStoredOhlcvAndAddsNewCandles()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage original = await Import(client, Header + Original);
        original.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage updated = await Import(client, Header + "2026-01-01T00:00:00Z,100,110,90,105,1\n2026-01-01T01:00:00Z,101,112,89,106,2.5\n");
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonArray candles = await Read(client);
        candles.Count.ShouldBe(2);
        candles[0]!["close"]!.GetValue<string>().ShouldBe("105");
        candles[1]!["open"]!.GetValue<string>().ShouldBe("101");
        candles[1]!["high"]!.GetValue<string>().ShouldBe("112");
        candles[1]!["low"]!.GetValue<string>().ShouldBe("89");
        candles[1]!["close"]!.GetValue<string>().ShouldBe("106");
        candles[1]!["volume"]!.GetValue<string>().ShouldBe("2.5");
    }

    [TestCase("2026-01-01T01:00:00,100,110,90,105,1\n")]
    [TestCase("2026-01-01T01:00:00+03:00,100,110,90,105,1\n")]
    [TestCase("2026-01-01T01:00:00Z,NaN,110,90,105,1\n")]
    [TestCase("2026-01-01T01:00:00Z,100,99,90,105,1\n")]
    [TestCase("2026-01-01T01:01:00Z,100,110,90,105,1\n")]
    [TestCase("")]
    public async Task InvalidCsvReturnsProblemWithoutSaving(string rows)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage response = await Import(client, Header + rows);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await Read(client)).Count.ShouldBe(0);
    }

    [Test]
    public async Task ConcurrentImportsSucceedAndKeepOneCompleteCandle()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        HttpResponseMessage[] responses = await Task.WhenAll(
            Import(client, Header + Original),
            Import(client, Header + Original.Replace(",105,", ",106,", StringComparison.Ordinal)));

        try
        {
            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
            JsonArray candles = await Read(client);
            candles.Count.ShouldBe(1);
            candles[0]!["close"]!.GetValue<string>().ShouldBeOneOf("105", "106");
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task ReadsOrderedHalfOpenRangeWithLimit()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage response = await Import(client, Header + Original + "2026-01-01T00:00:00Z,100,110,90,105,1\n2026-01-01T02:00:00Z,100,110,90,105,1\n");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var first = (await client.GetFromJsonAsync<JsonArray>(Candles(_symbol, "2026-01-01T02:00:00Z") + "&limit=1"))!;
        first.Count.ShouldBe(1);
        first[0]!["openTime"]!.GetValue<DateTimeOffset>().ShouldBe(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        (await client.GetFromJsonAsync<JsonArray>(Candles(_symbol, "2026-01-01T02:00:00Z")))!.Count.ShouldBe(2);
        (await client.GetFromJsonAsync<JsonArray>(Candles(_symbol + "X", "2026-01-01T03:00:00Z")))!.Count.ShouldBe(0);
    }

    [Test]
    public async Task OversizedMultipartReturnsBadRequestProblem()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage response = await Import(client, new string(c: 'x', 11 * 1024 * 1024));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await Read(client)).Count.ShouldBe(0);
    }

    [Test]
    public async Task IncompleteCandleReportsActualCsvLine_ThenImportsAtClose()
    {
        var clock = new TestClock { Now = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 1, minute: 30, second: 0, TimeSpan.Zero) };
        await using var application = new ProfitGraphApiFactory(clock);
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage response = await Import(client, "\n" + Header + "\n" + Original);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        problem["errors"]![0]!["field"]!.GetValue<string>().ShouldBe("File.Line[4]");
        (await Read(client)).Count.ShouldBe(0);

        clock.Now = clock.Now.AddMinutes(30);
        using HttpResponseMessage closed = await Import(client, Header + Original);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestCase("invalid,row\n")]
    [TestCase("2026-02-15T00:00:00Z,100,1,90,105,1\n")]
    [TestCase("2026-01-01T00:00:00Z,101,112,89,107,2.5\n")]
    public async Task ErrorAfterWrittenBatchRollsBackInsertsAndOverwrites(string invalidRow)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage original = await Import(client, Header + Original);
        original.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage failed = await Import(client, Header + BatchRows(count: 1000, close: 106) + invalidRow);

        failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonArray candles = await Read(client);
        candles.Count.ShouldBe(1);
        candles[0]!["close"]!.GetValue<string>().ShouldBe("105");
    }

    [Test]
    public async Task ConcurrentReversedBatchesDoNotDeadlockOrMixImports()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        HttpResponseMessage[] responses = await Task.WhenAll(
            Import(client, Header + BatchRows(count: 1001, close: 106)),
            Import(client, Header + BatchRows(count: 1001, close: 107, reverse: true)));

        try
        {
            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
            var candles = (await client.GetFromJsonAsync<JsonArray>(Candles(_symbol, "2026-03-01T00:00:00Z") + "&limit=2000"))!;
            candles.Count.ShouldBe(1001);
            candles.Select(candle => candle!["close"]!.GetValue<string>()).Distinct().Count().ShouldBe(1);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task UsesConfiguredRowAndErrorLimits()
    {
        await using var application = new ProfitGraphApiFactory(
            new Dictionary<string, string> { ["CsvImport:BatchSize"] = "2", ["CsvImport:MaxRows"] = "3", ["CsvImport:MaxDetailedErrors"] = "1" });
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage accepted = await Import(client, Header + BatchRows(count: 3, close: 106));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage tooMany = await Import(client, Header + BatchRows(count: 4, close: 107));
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonArray stored = await Read(client);
        stored.Count.ShouldBe(3);
        stored.ShouldAllBe(candle => candle!["close"]!.GetValue<string>() == "106");

        string invalid = Original.Replace(",110,", ",1,", StringComparison.Ordinal);
        using HttpResponseMessage badRows = await Import(client, Header + invalid + invalid + invalid);
        badRows.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await badRows.Content.ReadFromJsonAsync<JsonNode>())!;
        problem["errors"]!.AsArray().Count.ShouldBe(2);
        problem["errors"]![1]!["message"]!.GetValue<string>().ShouldContain("3 invalid candle rows");
    }

    [TestCase("2026-01-01T02:00:00Z,1e3,110,90,105,1\n", "File.Line[1003].open")]
    [TestCase("not-a-timestamp,100,110,90,105,1\n", "File.Line[1003].openTime")]
    [TestCase("2026-01-01T02:00:00Z,100,110\n", "File.Line[1003]")]
    public async Task FormatFailurePreservesEarlierDataErrorsAndRollsBack(string malformedRow, string expectedField)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        string invalid = Original.Replace(",110,", ",1,", StringComparison.Ordinal);
        using HttpResponseMessage response = await Import(client, Header + BatchRows(count: 1000, close: 106) + invalid + malformedRow);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonArray errors = (await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsArray();
        errors.Count.ShouldBe(3);
        errors[0]!["field"]!.GetValue<string>().ShouldBe("File.Line[1002]");
        errors[0]!["code"]!.GetValue<string>().ShouldBe("invalid_candle");
        errors[1]!["field"]!.GetValue<string>().ShouldBe(expectedField);
        errors[1]!["code"]!.GetValue<string>().ShouldBe("invalid_csv");
        errors[2]!["message"]!.GetValue<string>().ShouldContain("1 invalid candle rows found before reading stopped");
        (await Read(client)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReadsHistoryUsingTheVenueSymbolRule()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage imported = await Import(client, Header + Original);
        imported.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/historical-data/candles?venue=bybit&symbol=%20{_symbol[..4].ToLowerInvariant()}%2F{_symbol[4..].ToLowerInvariant()}%20&timeframe=%201h%20&from=2026-01-01T00:00:00Z&to=2026-01-01T03:00:00Z");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonArray>())!.Count.ShouldBe(1);
    }

    [Test]
    public async Task UsesConfiguredUploadLimit()
    {
        await using var application = new ProfitGraphApiFactory(new Dictionary<string, string> { ["CsvImport:MaxFileSizeBytes"] = "128" });
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);
        using HttpResponseMessage accepted = await Import(client, Header + Original);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage oversized = await Import(client, Header + Original + new string(c: '\n', count: 128));

        oversized.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        oversized.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Test]
    public async Task OnlyTheOwnerImportsButEveryUserReads()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient owner = await application.CreateSignedInClient(UserRole.Owner);
        using HttpClient user = await application.CreateSignedInClient();
        using HttpClient anonymous = application.CreateClient();

        using HttpResponseMessage forbidden = await Import(user, Header + Original);
        using HttpResponseMessage unauthorized = await Import(anonymous, Header + Original);
        using HttpResponseMessage imported = await Import(owner, Header + Original);

        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        imported.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Read(user)).Count.ShouldBe(1);
    }

    [TestCase("unknown", "BTCUSDT", "1h", "unknown_venue", "Venue")]
    [TestCase("", "BTCUSDT", "1h", "unknown_venue", "Venue")]
    [TestCase("bybit", "BTC-USDT", "1h", "invalid_symbol", "Symbol")]
    [TestCase("bybit", "BTCUSDT", "1w", "unsupported_timeframe", "Timeframe")]
    public async Task UnknownSeriesIsAValidationError(string venue, string symbol, string timeframe, string code, string field)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient(UserRole.Owner);

        using HttpResponseMessage import = await Import(client, Header + Original, venue, symbol, timeframe);
        using HttpResponseMessage read = await client.GetAsync(
            $"/api/historical-data/candles?venue={venue}&symbol={symbol}&timeframe={timeframe}&from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");

        foreach (HttpResponseMessage response in new[] { import, read })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            JsonNode error = (await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]![0]!;
            error["code"]!.GetValue<string>().ShouldBe(code);
            error["field"]!.GetValue<string>().ShouldBe(field);
        }
    }

    [TestCase("2026-01-02T00:00:00Z", arg2: 0)]
    [TestCase("2026-01-02T00:00:00Z", arg2: 10_001)]
    [TestCase("2026-01-01T00:00:00Z", arg2: 10)]
    public async Task CandleReadsAreBounded(string to, int limit)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();

        using HttpResponseMessage response = await client.GetAsync(Candles(_symbol, to) + $"&limit={limit}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static string Candles(string symbol, string to)
    {
        return $"/api/historical-data/candles?venue=bybit&symbol={symbol}&timeframe=1h&from=2026-01-01T00:00:00Z&to={to}";
    }

    private static string BatchRows(int count, int close, bool reverse = false)
    {
        IEnumerable<int> indices = Enumerable.Range(start: 0, count);

        if (reverse)
        {
            indices = indices.Reverse();
        }

        var start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

        return string.Concat(indices.Select(index => FormattableString.Invariant($"{start.AddHours(index):yyyy-MM-ddTHH:mm:ssZ},101,112,89,{close},2.5\n")));
    }

    private Task<HttpResponseMessage> Import(HttpClient client, string csv)
    {
        return Import(client, csv, "bybit", _symbol[..4] + "/" + _symbol[4..], "1h");
    }

    private static async Task<HttpResponseMessage> Import(HttpClient client, string csv, string venue, string symbol, string timeframe)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(venue), "Venue");
        form.Add(new StringContent(symbol), "Symbol");
        form.Add(new StringContent(timeframe), "Timeframe");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "File", "candles.csv");

        return await client.PostAsync("/api/historical-data/csv", form);
    }

    private async Task<JsonArray> Read(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(Candles(_symbol, "2026-01-01T03:00:00Z"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonArray>())!;
    }
}