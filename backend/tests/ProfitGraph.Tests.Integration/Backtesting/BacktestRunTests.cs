using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     Runs started in the API and executed by a Worker against the test venue (ADR 0012).
/// </summary>
[NonParallelizable]
public sealed class BacktestRunTests
{
    private static readonly DateTimeOffset First = FakeHistorySource.DefaultFirst;
    private static readonly DateTimeOffset From = First.AddDays(2);
    private static readonly DateTimeOffset To = First.AddDays(12);
    private ProfitGraphApiFactory _application = null!;
    private HttpClient _client = null!;
    private Guid _userId;
    private string _symbol = string.Empty;

    private static DateTimeOffset Tomorrow => new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);

    private static Func<JsonNode, bool> Finished => job => job["status"]!.GetValue<string>() is "Completed" or "Failed" or "Cancelled";

    [SetUp]
    public async Task StartApi()
    {
        _application = new ProfitGraphApiFactory();
        _client = await _application.CreateSignedInClient();
        _userId = await JobScenario.UserId(_client);
        _symbol = "B" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "USDT";
    }

    [TearDown]
    public async Task StopApi()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Test]
    public async Task RunReplaysPreparedHistory_AndStoresTheFactsOfADirectReplay()
    {
        using IHost worker = await StartWorker(Wave);
        Guid strategyId = await CreateStrategy();

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/backtests", Request(strategyId));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var id = (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
        response.Headers.Location!.ToString().ShouldEndWith($"/api/jobs/{id}");
        JsonNode job = await JobScenario.WaitFor(_client, id, Finished);
        await worker.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());
        job["result"]!["processedCandles"]!.GetValue<int>().ShouldBe(240);
        job["completed"]!.GetValue<long>().ShouldBe(240);
        job["total"]!.GetValue<long>().ShouldBe(240);
        BacktestResult expected = await DirectReplay(strategyId);
        expected.Ledger.Count.ShouldBeGreaterThan(expected: 0, "The wave must trade for the comparison to mean anything.");
        BacktestRunRow run = await Run(id);
        run.ShouldSatisfyAllConditions(
            () => run.Stage.ShouldBe("replaying"),
            () => run.Outcome.ShouldBe("completed"),
            () => run.ProcessedCandles.ShouldBe(240),
            () => run.ReplayCount.ShouldBe(240),
            () => run.TotalPnl.ShouldBe(expected.Terminal.TotalPnl),
            () => run.CandleCount.ShouldBe(240),
            () => run.LedgerCount.ShouldBe(expected.Ledger.Count),
            () => run.HasMetrics.ShouldBeTrue());
        (await Scalar<bool>(
                "SELECT (SELECT jsonb_agg(snapshot ORDER BY sequence) FROM backtest_ledger WHERE run_id = @id) = CAST(@expected AS jsonb)",
                new { id, expected = BacktestJsonSerializer.Serialize(expected.Ledger) }))
            .ShouldBeTrue();
        (await Scalar<long>("SELECT ledger_through FROM backtest_candles WHERE run_id = @id ORDER BY sequence DESC LIMIT 1", new { id }))
            .ShouldBe(expected.Ledger.Count);
    }

    [Test]
    public async Task CancellingDuringPreparationKeepsTheRunWithoutAResult()
    {
        using var gate = new SemaphoreSlim(initialCount: 0);
        using IHost worker = await StartWorker((timeframe, from, to) =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));

            return FakeHistorySource.Complete(timeframe, from, to);
        });
        Guid id = await Start(await CreateStrategy());
        await JobScenario.WaitFor(_client, id, JobScenario.Status("Running"));

        (await _client.PostAsync($"/api/jobs/{id}/cancel", content: null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        gate.Release(releaseCount: 100);
        JsonNode job = await JobScenario.WaitFor(_client, id, Finished);
        await worker.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Cancelled", job.ToJsonString());
        BacktestRunRow run = await Run(id);
        run.Stage.ShouldBe("loading_history");
        run.Outcome.ShouldBeNull();
    }

    [Test]
    public async Task WorkerShutdownInterruptsARun()
    {
        using var gate = new SemaphoreSlim(initialCount: 0);
        IHost worker = await StartWorker((timeframe, from, to) =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));

            return FakeHistorySource.Complete(timeframe, from, to);
        });
        Guid id = await Start(await CreateStrategy());
        await JobScenario.WaitFor(_client, id, JobScenario.Status("Running"));

        Task stopping = worker.StopAsync();
        gate.Release(releaseCount: 100);
        await stopping;
        worker.Dispose();
        JsonNode job = await JobScenario.WaitFor(_client, id, Finished);

        job["failure"]!["code"]!.GetValue<string>().ShouldBe("background_job_interrupted");
        (await Run(id)).Outcome.ShouldBeNull();
    }

    [Test]
    public async Task ChangedCompilationFailsTheRunBeforeItStarts()
    {
        Guid id = await Start(await CreateStrategy());
        await Execute(
            "UPDATE backtest_runs SET configuration = jsonb_set(configuration, '{fingerprint}', to_jsonb(repeat('0', 64))) WHERE id = @id",
            new { id });
        using IHost worker = await StartWorker(candles: null);

        JsonNode job = await JobScenario.WaitFor(_client, id, Finished);
        await worker.StopAsync();

        job["failure"]!["code"]!.GetValue<string>().ShouldBe(BacktestErrorCodes.CompilationChanged);
        (await Run(id)).Stage.ShouldBeNull();
    }

    [Test]
    public async Task AnotherUsersStrategyIsMissing()
    {
        Guid strategyId = await CreateStrategy();
        using HttpClient other = await _application.CreateSignedInClient();

        HttpResponseMessage response = await other.PostAsJsonAsync("/api/backtests", Request(strategyId));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonNode>())!["code"]!.GetValue<string>().ShouldBe("strategy_not_found");
    }

    [TestCase("unknown parameter", BacktestErrorCodes.UnknownParameter)]
    [TestCase("venue without rules", InstrumentRulesErrorCodes.InvalidRequest)]
    [TestCase("holdout leaves no sample", BacktestErrorCodes.InvalidSplit)]
    [TestCase("inexact number", "invalid_number")]
    [TestCase("future start", "invalid_request")]
    public async Task InvalidRequestsAreRejectedBeforeQueueing(string mode, string code)
    {
        Guid strategyId = await CreateStrategy();
        object request = mode switch
        {
            "unknown parameter" => Request(strategyId, parameters: new Dictionary<string, string> { ["missing"] = "1" }),
            "venue without rules" => Request(strategyId, TestHistoryVenue.WithoutHistory),
            "holdout leaves no sample" => Request(strategyId, to: From.AddHours(1), outOfSampleFraction: "0.5"),
            "inexact number" => Request(strategyId, startingCapital: "1e3"),
            _ => Request(strategyId, from: Tomorrow.AddDays(1), to: Tomorrow.AddDays(2))
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/backtests", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsArray()
            .Select(error => error!["code"]?.GetValue<string>())
            .ShouldContain(code, await response.Content.ReadAsStringAsync());
        (await Scalar<long>("SELECT count(*) FROM backtest_runs WHERE strategy_id = @strategyId", new { strategyId })).ShouldBe(0);
    }

    /// <summary>
    ///     A price wave that makes the fast SMA cross the slow one again and again.
    /// </summary>
    private static IReadOnlyList<Candle> Wave(Timeframe timeframe, DateTimeOffset from, DateTimeOffset to)
    {
        var candles = new List<Candle>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            long index = (time.UtcTicks - First.UtcTicks) / timeframe.Duration.Ticks;
            decimal open = Price(index - 1);
            decimal close = Price(index);
            candles.Add(new Candle(time, open, Math.Max(open, close) + 1m, Math.Min(open, close) - 1m, close, Volume: 10m));
        }

        return candles;
    }

    private static decimal Price(long index)
    {
        return 100m + (index % 12 < 6 ? index % 12 : 12 - index % 12) * 2m;
    }

    private async Task<IHost> StartWorker(Func<Timeframe, DateTimeOffset, DateTimeOffset, IReadOnlyList<Candle>>? candles)
    {
        IHost worker = WorkerHost.Create();
        var history = worker.Services.GetRequiredService<FakeHistorySource>();
        history.Reset();
        history.Candles = candles;
        await worker.StartAsync();

        return worker;
    }

    private async Task<Guid> CreateStrategy()
    {
        using IServiceScope scope = _application.Services.CreateScope();
        StrategyInternal created = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(
                new CreateStrategyCommand(
                    _userId,
                    "Wave SMA",
                    SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 4).ToInternal(),
                    new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())));

        return created.Id;
    }

    private async Task<Guid> Start(Guid strategyId)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/backtests", Request(strategyId));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
    }

    private object Request(
        Guid strategyId,
        string venue = TestHistoryVenue.WithHistory,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        Dictionary<string, string>? parameters = null,
        string startingCapital = "1000",
        string outOfSampleFraction = "0.25")
    {
        return new
        {
            strategyId,
            venue,
            symbol = _symbol,
            timeframe = "1h",
            from = from ?? From,
            to = to ?? To,
            parameters = parameters ?? new Dictionary<string, string> { ["buyAmount"] = "50" },
            startingCapital,
            slippage = "0.001",
            limitFeeRate = "0.001",
            marketFeeRate = "0.001",
            outOfSampleFraction
        };
    }

    /// <summary>
    ///     The same strategy, parameters, rules and cached history replayed directly by the engine.
    /// </summary>
    private async Task<BacktestResult> DirectReplay(Guid strategyId)
    {
        using IServiceScope scope = _application.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        var strategies = services.GetRequiredService<IStrategyRepository>();
        StrategyVersion version = (await strategies.GetVersion(
            new StrategyId(strategyId),
            StrategyVersionNumber.First,
            CancellationToken.None))!;
        CompiledTradingGraph plan = services.GetRequiredService<BacktestCompiler>()
            .Compile(version.Definition, new Dictionary<string, decimal> { ["buyAmount"] = 50m });
        CandleSeries series = services.GetRequiredService<VenueRegistry>().ResolveSeries(TestHistoryVenue.WithHistory, _symbol, "1h");
        PreparedHistory prepared = await services.GetRequiredService<HistoryPreparationService>()
            .Prepare(series, From, To, plan.WarmupCandles, DateTimeOffset.UtcNow, reportProgress: false, CancellationToken.None);
        var input = new BacktestInput(
            plan,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0.001m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            FakeInstrumentSource.DefaultRules,
            prepared.ToReplay(),
            OutOfSampleSplit.Create(0.25m));

        return new BacktestEngine().Run(input);
    }

    private Task<BacktestRunRow> Run(Guid id)
    {
        return Scalar<BacktestRunRow>(
            sql: """
                 SELECT stage AS Stage, outcome AS Outcome, processed_candles AS ProcessedCandles, replay_count AS ReplayCount, total_pnl AS TotalPnl,
                     candle_count AS CandleCount, ledger_count AS LedgerCount, metrics IS NOT NULL AS HasMetrics
                 FROM backtest_runs WHERE id = @id
                 """,
            new { id });
    }

    private async Task<T> Scalar<T>(string sql, object parameters)
    {
        using IServiceScope scope = _application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<NpgsqlConnection>().QuerySingleAsync<T>(sql, parameters);
    }

    private async Task Execute(string sql, object parameters)
    {
        using IServiceScope scope = _application.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NpgsqlConnection>().ExecuteAsync(sql, parameters);
    }
}