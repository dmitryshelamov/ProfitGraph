using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Dapper;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     Reads of runs and their facts (ADR 0012): details, bounded pages, the chart, lists, the ranking and ownership.
/// </summary>
[NonParallelizable]
public sealed class BacktestResultTests
{
    private static readonly Dictionary<string, string?> CompactAtOnce = new Dictionary<string, string?>
    {
        ["Backtests:CompactAfter"] = "00:00:00",
        ["Backtests:CompactionInterval"] = "00:00:00.050"
    };

    private static readonly Dictionary<string, string?> CompactNever = new Dictionary<string, string?> { ["Backtests:CompactAfter"] = "1.00:00:00" };
    private ProfitGraphApiFactory _application = null!;
    private HttpClient _client = null!;
    private Guid _userId;
    private string _symbol = string.Empty;

    [SetUp]
    public async Task StartApi()
    {
        _application = new ProfitGraphApiFactory();
        _client = await _application.CreateSignedInClient();
        _userId = await JobScenario.UserId(_client);
        _symbol = BacktestScenario.NewSymbol();
    }

    [TearDown]
    public async Task StopApi()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Test]
    public async Task CompletedRunHasDetailsFactPagesAndAChart()
    {
        Guid id = await CompletedRun(BacktestScenario.Request(await BacktestScenario.CreateStrategy(_application, _userId), _symbol));
        await Compact(id);

        JsonNode run = await Get($"/api/backtests/{id}");
        run["status"]!.GetValue<string>().ShouldBe("Completed");
        run["stage"]!.GetValue<string>().ShouldBe("replaying");
        run["configuration"]!["parameters"]!["buyAmount"]!.GetValue<string>().ShouldBe("50");
        run["configuration"]!["outOfSampleFraction"]!.GetValue<string>().ShouldBe("0.25");
        run["history"]!["replayCount"]!.GetValue<int>().ShouldBe(240);
        run["history"]!["quoteAsset"]!.GetValue<string>().ShouldBe("USDT");
        run["history"]!["rules"]!["priceTick"]!.GetValue<string>().ShouldBe("0.01");
        run["result"]!["outcome"]!.GetValue<string>().ShouldBe("completed");
        run["result"]!["metrics"]!["full"]!["observedCandles"]!.GetValue<int>().ShouldBe(240);
        run["result"]!["metrics"]!["outOfSample"]!["observedCandles"]!.GetValue<int>().ShouldBe(60);
        run["chartBucketSize"]!.GetValue<int>().ShouldBe(1);
        run["candleCount"]!.GetValue<long>().ShouldBe(240);

        JsonNode[] fills = await ReadAll($"/api/backtests/{id}/fills", limit: 7);
        fills.Length.ShouldBe((int)await Scalar<long>("SELECT count(*) FROM backtest_ledger WHERE run_id = @id AND kind = 'Filled'", new { id }));
        fills.Length.ShouldBeGreaterThan(0);
        fills.ShouldAllBe(fill => fill["kind"]!.GetValue<string>() == "Filled" && fill["fill"] != null);
        fills.Select(fill => fill["sequence"]!.GetValue<long>()).ShouldBeInOrder(SortDirection.Ascending);
        JsonNode[] orders = await ReadAll($"/api/backtests/{id}/orders", limit: 5);
        orders.Select(order => order["order"]!["id"]!.GetValue<long>()).ShouldBeUnique();
        orders.Length.ShouldBe((int)await Scalar<long>("SELECT count(DISTINCT order_id) FROM backtest_ledger WHERE run_id = @id", new { id }));
        (await ReadAll($"/api/backtests/{id}/diagnostics", limit: 3)).Length.ShouldBe((int)run["diagnosticCount"]!.GetValue<long>());

        JsonNode chart = await Get($"/api/backtests/{id}/chart");
        JsonArray points = chart["points"]!.AsArray();
        chart["bucketSize"]!.GetValue<int>().ShouldBe(1);
        points.Count.ShouldBe(240);
        points.Sum(point => point!["buyFills"]!.GetValue<int>() + point["sellFills"]!.GetValue<int>()).ShouldBe(fills.Length);
        JsonNode observations = await Get($"/api/backtests/{id}/observations");
        observations["items"]!.AsArray().Select(item => item!["sequence"]!.GetValue<long>()).ShouldBe([240L]);
    }

    [Test]
    public async Task EveryObservationCarriesItsOwnFacts_AndTheLiveChartEqualsTheCompactedOne()
    {
        Guid id = await CompletedRun(BacktestScenario.Request(await BacktestScenario.CreateStrategy(_application, _userId), _symbol));

        JsonNode[] observations = await ReadAll($"/api/backtests/{id}/observations", limit: 50);
        observations.Select(item => item["sequence"]!.GetValue<long>()).ShouldBe(Enumerable.Range(start: 1, count: 240).Select(index => (long)index));
        long[] ledger = [.. observations.SelectMany(item => item["ledger"]!.AsArray()).Select(entry => entry!["sequence"]!.GetValue<long>())];
        ledger.ShouldNotBeEmpty();
        ledger.ShouldBe(Enumerable.Range(start: 1, ledger.Length).Select(index => (long)index));
        observations.SelectMany(item => item["diagnostics"]!.AsArray())
            .Select(diagnostic => diagnostic!["sequence"]!.GetValue<long>())
            .ShouldBeInOrder(SortDirection.Ascending);
        (await Get($"/api/backtests/{id}"))["chartBucketSize"].ShouldBeNull();
        JsonNode live = await Get($"/api/backtests/{id}/chart");

        await Compact(id);

        (await Get($"/api/backtests/{id}/chart")).ToJsonString().ShouldBe(live.ToJsonString());
        (await Get($"/api/backtests/{id}/observations"))["items"]!.AsArray().Count.ShouldBe(1);
    }

    [Test]
    public async Task ReadsOfAnotherUsersRunAreMissing()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid id = await BacktestScenario.Start(_client, BacktestScenario.Request(strategyId, _symbol));
        using HttpClient other = await _application.CreateSignedInClient();

        foreach (string path in new[] { "", "/observations", "/orders", "/fills", "/diagnostics", "/chart" })
        {
            foreach (Guid run in new[] { id, Guid.NewGuid() })
            {
                HttpResponseMessage response = await other.GetAsync($"/api/backtests/{run}{path}");
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
                (await response.Content.ReadFromJsonAsync<JsonNode>())!["code"]!.GetValue<string>().ShouldBe(BacktestErrorCodes.RunNotFound);
            }
        }

        HttpResponseMessage list = await other.GetAsync($"/api/strategies/{strategyId}/backtests");
        list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetFromJsonAsync<JsonNode>("/api/backtests?quote=USDT"))!["items"]!.AsArray().ShouldBeEmpty();
    }

    [Test]
    public async Task StrategyRunsAreNewestFirst_TheListShowsTheLatest_AndTheRankingSortsByReturn()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid small = await CompletedRun(BacktestScenario.Request(strategyId, _symbol));
        Guid large = await CompletedRun(BacktestScenario.Request(strategyId, _symbol, parameters: new Dictionary<string, string> { ["buyAmount"] = "300" }));

        JsonNode first = await Get($"/api/strategies/{strategyId}/backtests?limit=1");
        JsonNode second = await Get($"/api/strategies/{strategyId}/backtests?limit=1&after={first["next"]!.GetValue<Guid>()}");
        first["items"]![0]!["id"]!.GetValue<Guid>().ShouldBe(large);
        second["items"]![0]!["id"]!.GetValue<Guid>().ShouldBe(small);
        second["next"].ShouldBeNull();
        JsonNode latest = (await _client.GetFromJsonAsync<JsonArray>("/api/strategies"))!.Single(item => item!["id"]!.GetValue<Guid>() == strategyId)!["latestRun"]!;
        latest["id"]!.GetValue<Guid>().ShouldBe(large);
        latest["status"]!.GetValue<string>().ShouldBe("Completed");

        JsonNode[] ranking = await ReadAllRuns($"/api/backtests?quote=USDT&symbol={_symbol}&timeframe=1h", limit: 1);
        ranking.Select(item => item["id"]!.GetValue<Guid>()).ShouldBe([small, large], ignoreOrder: true);
        ranking.Select(item => decimal.Parse(item["returnPercent"]!.GetValue<string>(), CultureInfo.InvariantCulture)).ShouldBeInOrder(SortDirection.Descending);
        (await Get($"/api/backtests?quote=USDT&symbol={_symbol}&timeframe=4h"))["items"]!.AsArray().ShouldBeEmpty();
    }

    [TestCase("pnl", "asc", "totalPnl")]
    [TestCase("pnl", "desc", "totalPnl")]
    [TestCase("drawdown", "asc", "maxDrawdownPercent")]
    [TestCase("return", "asc", "returnPercent")]
    public async Task TheRankingSortsByTheChosenColumnAcrossPages(string sort, string order, string field)
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);

        foreach (string amount in new[] { "50", "300", "150" })
        {
            await CompletedRun(BacktestScenario.Request(strategyId, _symbol, parameters: new Dictionary<string, string> { ["buyAmount"] = amount }));
        }

        JsonNode[] ranking = await ReadAllRuns($"/api/backtests?quote=USDT&symbol={_symbol}&sort={sort}&order={order}", limit: 1);

        ranking.Length.ShouldBe(3);
        ranking.Select(item => decimal.Parse(item[field]!.GetValue<string>(), CultureInfo.InvariantCulture))
            .ShouldBeInOrder(order == "asc" ? SortDirection.Ascending : SortDirection.Descending);
        ranking.ShouldAllBe(item => item["maxDrawdownPercent"] != null);
    }

    [Test]
    public async Task TheRankingFiltersTheOwnersRuns_ListsCancelledRunsByStartTime_AndCountsEveryFilterValue()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow.AddMinutes(-5);
        Guid wave = await BacktestScenario.CreateStrategy(_application, _userId, "Wave alpha");
        Guid other = await BacktestScenario.CreateStrategy(_application, _userId, "Other beta");
        Guid small = await CompletedRun(BacktestScenario.Request(wave, _symbol));
        Guid large = await CompletedRun(BacktestScenario.Request(wave, _symbol, parameters: new Dictionary<string, string> { ["buyAmount"] = "300" }));
        Guid third = await CompletedRun(BacktestScenario.Request(other, _symbol));
        Guid cancelled = await BacktestScenario.Start(_client, BacktestScenario.Request(wave, _symbol));
        JsonNode queued = (await Get("/api/jobs/active"))["jobs"]!.AsArray().Single()!;
        queued.ShouldSatisfyAllConditions(
            () => queued["id"]!.GetValue<Guid>().ShouldBe(cancelled),
            () => queued["kind"]!.GetValue<string>().ShouldBe("backtest"),
            () => queued["lane"]!.GetValue<string>().ShouldBe("main"),
            () => queued["status"]!.GetValue<string>().ShouldBe("Queued"),
            () => queued["queuePosition"]!.GetValue<int>().ShouldBeGreaterThan(0),
            () => queued["strategyId"]!.GetValue<Guid>().ShouldBe(wave),
            () => queued["strategyName"]!.GetValue<string>().ShouldBe("Wave alpha"),
            () => queued["venue"]!.GetValue<string>().ShouldBe(TestHistoryVenue.WithHistory),
            () => queued["symbol"]!.GetValue<string>().ShouldBe(_symbol),
            () => queued["timeframe"]!.GetValue<string>().ShouldBe("1h"));
        (await _client.PostAsync($"/api/jobs/{cancelled}/cancel", content: null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        JsonNode[] completed = await ReadAllRuns("/api/backtests?quote=USDT", limit: 2);
        decimal best = completed.Max(item => decimal.Parse(item["returnPercent"]!.GetValue<string>(), CultureInfo.InvariantCulture));
        decimal calmest = completed.Min(item => decimal.Parse(item["maxDrawdownPercent"]!.GetValue<string>(), CultureInfo.InvariantCulture));

        completed.Select(item => item["id"]!.GetValue<Guid>()).ShouldBe([small, large, third], ignoreOrder: true);
        completed.ShouldAllBe(item => item["optimizationId"] == null);
        (await Ids($"/api/backtests?quote=USDT&strategyId={wave}")).ShouldBe([small, large], ignoreOrder: true);
        (await Ids("/api/backtests?quote=USDT&search=ALPHA")).ShouldBe([small, large], ignoreOrder: true);
        (await Ids($"/api/backtests?quote=USDT&search={_symbol[2..7].ToLowerInvariant()}")).Length.ShouldBe(3);
        (await Ids("/api/backtests?quote=USDT&search=%25")).ShouldBeEmpty();
        (await Ids($"/api/backtests?quote=USDT&venue={TestHistoryVenue.WithHistory}&timeframe=1h&origin=manual")).Length.ShouldBe(3);
        (await Ids("/api/backtests?quote=USDT&venue=nowhere")).ShouldBeEmpty();
        (await Ids("/api/backtests?quote=USDT&origin=optimization")).ShouldBeEmpty();
        (await Ids($"/api/backtests?quote=USDT&from={Time(started)}&to={Time(DateTimeOffset.UtcNow.AddMinutes(5))}")).Length.ShouldBe(3);
        (await Ids($"/api/backtests?quote=USDT&from={Time(DateTimeOffset.UtcNow.AddMinutes(5))}")).ShouldBeEmpty();
        (await Ids($"/api/backtests?quote=USDT&minReturn={best.ToString(CultureInfo.InvariantCulture)}")).Length.ShouldBe(1);
        (await Ids($"/api/backtests?quote=USDT&maxDrawdown={calmest.ToString(CultureInfo.InvariantCulture)}")).ShouldNotBeEmpty();
        (await Ids($"/api/backtests?quote=USDT&maxDrawdown={(calmest - 0.0001m).ToString(CultureInfo.InvariantCulture)}")).ShouldBeEmpty();
        (await Ids("/api/backtests?status=cancelled&sort=created")).ShouldBe([cancelled]);
        (await Ids("/api/backtests?status=failed&sort=created")).ShouldBeEmpty();
        (await Get("/api/jobs/active"))["jobs"]!.AsArray().ShouldBeEmpty();

        JsonNode facets = await Get("/api/backtests/facets");
        Counts(facets["quotes"]!).ShouldBe(new Dictionary<string, int> { ["USDT"] = 3 });
        Counts(facets["statuses"]!).ShouldBe(new Dictionary<string, int> { ["completed"] = 3, ["cancelled"] = 1 });
        Counts(facets["origins"]!).ShouldBe(new Dictionary<string, int> { ["manual"] = 4 });
        Counts(facets["venues"]!).ShouldBe(new Dictionary<string, int> { [TestHistoryVenue.WithHistory] = 4 });
        Counts(facets["timeframes"]!).ShouldBe(new Dictionary<string, int> { ["1h"] = 4 });
        facets["strategies"]!.AsArray()
            .Select(item => (item!["id"]!.GetValue<Guid>(), item["name"]!.GetValue<string>(), item["count"]!.GetValue<int>()))
            .ShouldBe([(wave, "Wave alpha", 3), (other, "Other beta", 1)]);
        using HttpClient stranger = await _application.CreateSignedInClient();
        Counts((await stranger.GetFromJsonAsync<JsonNode>("/api/backtests/facets"))!["statuses"]!).ShouldBeEmpty();
    }

    [TestCase("/api/backtests?quote=USDT&sort=sharpe", "invalid_sort")]
    [TestCase("/api/backtests?quote=USDT&order=up", "invalid_order")]
    [TestCase("/api/backtests?quote=usdt", "invalid_quote")]
    [TestCase("/api/backtests?quote=USDT&timeframe=7m", "unsupported_timeframe")]
    [TestCase("/api/backtests?quote=USDT&limit=101", "InclusiveBetweenValidator")]
    [TestCase("/api/backtests?quote=USDT&status=done", "invalid_status")]
    [TestCase("/api/backtests?quote=USDT&origin=bot", "invalid_origin")]
    [TestCase("/api/backtests?quote=USDT&venue=Bad", "invalid_venue")]
    [TestCase("/api/backtests?quote=USDT&from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z", "invalid_period")]
    [TestCase("/api/backtests?quote=USDT&minReturn=1e3", "invalid_percent")]
    [TestCase("/api/backtests?status=failed", "sort_needs_result")]
    [TestCase("/api/backtests?status=failed&sort=created&minReturn=1", "result_filter_needs_result")]
    [TestCase("/api/backtests?status=cancelled&sort=created&quote=usdt", "invalid_quote")]
    [TestCase("/api/backtests/{id}/fills?limit=0", "InclusiveBetweenValidator")]
    [TestCase("/api/backtests/{id}/observations?limit=101", "InclusiveBetweenValidator")]
    [TestCase("/api/backtests/{id}/orders?after=-1", "GreaterThanOrEqualValidator")]
    public async Task InvalidReadsAreRejected(string path, string code)
    {
        Guid id = await BacktestScenario.Start(_client, BacktestScenario.Request(await BacktestScenario.CreateStrategy(_application, _userId), _symbol));

        HttpResponseMessage response = await _client.GetAsync(path.Replace("{id}", id.ToString(), StringComparison.Ordinal));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).ShouldContain(code);
    }

    /// <summary>
    ///     Starts a Worker that compacts finished runs at once and waits until it compacted the run.
    /// </summary>
    private async Task Compact(Guid id)
    {
        using IHost compactor = WorkerHost.Create(settings: CompactAtOnce);
        await compactor.StartAsync();
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);

        while ((await Get($"/api/backtests/{id}"))["chartBucketSize"] is null)
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "The run was not compacted.");
            await Task.Delay(millisecondsDelay: 25);
        }

        await compactor.StopAsync();
    }

    [Test]
    public async Task SellerFeesAndHoldAreFrozenWithTheRunAndOmittedWithoutThem()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid withFees = await CompletedRun(
            new
            {
                strategyId,
                venue = TestHistoryVenue.WithHistory,
                symbol = _symbol,
                timeframe = "1h",
                from = BacktestScenario.From,
                to = BacktestScenario.To,
                parameters = new Dictionary<string, string> { ["buyAmount"] = "50" },
                startingCapital = "1000",
                slippage = "0.001",
                outOfSampleFraction = "0.25",
                sellerFees = new { rates = new[] { "0.05", "0.10" }, minimum = "0.01" },
                settlementHoldSeconds = 7200
            });
        Guid withRates = await CompletedRun(BacktestScenario.Request(strategyId, _symbol));
        Guid withIncrement = await CompletedRun(
            new
            {
                strategyId,
                venue = TestHistoryVenue.WithHistory,
                symbol = _symbol,
                timeframe = "1h",
                from = BacktestScenario.From,
                to = BacktestScenario.To,
                parameters = new Dictionary<string, string> { ["buyAmount"] = "50" },
                startingCapital = "1000",
                sellerFees = new { rates = new[] { "0.05", "0.10" }, minimum = "0.01", increment = 100 }
            });

        JsonNode configuration = (await Get($"/api/backtests/{withFees}"))["configuration"]!;
        configuration["limitFeeRate"]!.GetValue<string>().ShouldBe("0");
        configuration["sellerFees"]!["rates"]!.AsArray().Select(rate => rate!.GetValue<string>()).ShouldBe(["0.05", "0.1"]);
        configuration["sellerFees"]!["minimum"]!.GetValue<string>().ShouldBe("0.01");
        configuration["settlementHoldSeconds"]!.GetValue<long>().ShouldBe(7200);
        configuration["sellerFees"]!["increment"]!.GetValue<long>().ShouldBe(1);
        (await Scalar<bool>("SELECT configuration->'sellerFees' ? 'increment' FROM backtest_runs WHERE id = @id", new { id = withFees })).ShouldBeFalse();
        (await Get($"/api/backtests/{withIncrement}"))["configuration"]!["sellerFees"]!["increment"]!.GetValue<long>().ShouldBe(100);
        (await Scalar<long>("SELECT (configuration->'sellerFees'->>'increment')::bigint FROM backtest_runs WHERE id = @id", new { id = withIncrement })).ShouldBe(100);
        (await Get($"/api/backtests/{withRates}"))["configuration"]!["sellerFees"].ShouldBeNull();
        (await Get($"/api/backtests/{withRates}"))["configuration"]!["settlementHoldSeconds"].ShouldBeNull();
        (await Scalar<bool>("SELECT configuration ? 'settlementHoldSeconds' FROM backtest_runs WHERE id = @id", new { id = withRates })).ShouldBeFalse();
        (await Scalar<bool>("SELECT configuration ? 'sellerFees' FROM backtest_runs WHERE id = @id", new { id = withRates })).ShouldBeFalse();
    }

    [Test]
    public async Task SellerFeesWithRatesAreRejected()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/backtests",
            new
            {
                strategyId = await BacktestScenario.CreateStrategy(_application, _userId),
                venue = TestHistoryVenue.WithHistory,
                symbol = _symbol,
                timeframe = "1h",
                from = BacktestScenario.From,
                to = BacktestScenario.To,
                startingCapital = "1000",
                limitFeeRate = "0.001",
                sellerFees = new { rates = new[] { "0.05" }, minimum = "0.01" }
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [TestCase(0L)]
    [TestCase(10_001L)]
    public async Task ASellerFeeIncrementOutsideItsRangeIsRejected(long increment)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/backtests",
            new
            {
                strategyId = await BacktestScenario.CreateStrategy(_application, _userId),
                venue = TestHistoryVenue.WithHistory,
                symbol = _symbol,
                timeframe = "1h",
                from = BacktestScenario.From,
                to = BacktestScenario.To,
                startingCapital = "1000",
                sellerFees = new { rates = new[] { "0.05" }, minimum = "0.01", increment }
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [TestCase(-1L)]
    [TestCase(315_360_001L)]
    public async Task ASettlementHoldOutsideItsRangeIsRejected(long seconds)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/backtests",
            new
            {
                strategyId = await BacktestScenario.CreateStrategy(_application, _userId),
                venue = TestHistoryVenue.WithHistory,
                symbol = _symbol,
                timeframe = "1h",
                from = BacktestScenario.From,
                to = BacktestScenario.To,
                startingCapital = "1000",
                settlementHoldSeconds = seconds
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CompletedRun(object request)
    {
        using IHost worker = WorkerHost.Create(settings: CompactNever);
        var history = worker.Services.GetRequiredService<FakeHistorySource>();
        history.Reset();
        history.Candles = BacktestScenario.Wave;
        await worker.StartAsync();
        Guid id = await BacktestScenario.Start(_client, request);
        JsonNode job = await JobScenario.WaitFor(_client, id, BacktestScenario.Finished);
        await worker.StopAsync();
        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());

        return id;
    }

    private async Task<JsonNode> Get(string path)
    {
        HttpResponseMessage response = await _client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private async Task<JsonNode[]> ReadAll(string path, int limit)
    {
        var items = new List<JsonNode>();
        long? after = 0;

        while (after is not null)
        {
            JsonNode page = await Get($"{path}?after={after}&limit={limit}");
            JsonArray batch = page["items"]!.AsArray();
            batch.Count.ShouldBeLessThanOrEqualTo(limit);
            items.AddRange(batch.Select(item => item!));
            after = page["next"]?.GetValue<long>();
        }

        return [.. items];
    }

    private static Dictionary<string, int> Counts(JsonNode facet)
    {
        return facet.AsArray().ToDictionary(item => item!["value"]!.GetValue<string>(), item => item!["count"]!.GetValue<int>());
    }

    private static string Time(DateTimeOffset time)
    {
        return Uri.EscapeDataString(time.ToString("O", CultureInfo.InvariantCulture));
    }

    private async Task<Guid[]> Ids(string path)
    {
        return [.. (await ReadAllRuns(path, limit: 100)).Select(item => item["id"]!.GetValue<Guid>())];
    }

    private async Task<JsonNode[]> ReadAllRuns(string path, int limit)
    {
        var items = new List<JsonNode>();
        var cursor = string.Empty;

        while (true)
        {
            JsonNode page = await Get($"{path}&limit={limit}{cursor}");
            items.AddRange(page["items"]!.AsArray().Select(item => item!));

            if (page["next"] is not { } next)
            {
                return [.. items];
            }

            cursor = $"&after={next.GetValue<Guid>()}";
        }
    }

    private async Task<T> Scalar<T>(string sql, object parameters)
    {
        using IServiceScope scope = _application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<NpgsqlConnection>().QuerySingleAsync<T>(sql, parameters);
    }
}