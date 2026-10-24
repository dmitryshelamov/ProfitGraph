using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Optimization;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Optimization;

/// <summary>
///     Progress over <c>/ws/optimizations</c> with the real SignalR client: the Worker's trial batches and the job's end
///     reach the API through PostgreSQL <c>LISTEN/NOTIFY</c> (stage 4 plan, S4-12).
/// </summary>
[NonParallelizable]
public sealed class OptimizationProgressHubTests
{
    private const int TrialCount = 180;
    private ProfitGraphApiFactory _application = null!;
    private UserInternal _user = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task StartApi()
    {
        _application = new ProfitGraphApiFactory();
        _user = await _application.CreateAccount(UserRole.User);
        _client = await _application.SignIn(_user);
    }

    [TearDown]
    public async Task StopApi()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Test]
    public async Task EachConnectionGetsTrialsInOrderOnce_AndAReconnectingClientFillsItsGapFromTheCursor()
    {
        Guid id = await Start();
        var first = new OptimizationProgressClient(await Connect(_user));
        var cursor = await first.Connection.InvokeAsync<JsonElement>("Subscribe", id);
        cursor.GetProperty("lastSequence").GetInt64().ShouldBe(0);
        cursor.GetProperty("status").GetString().ShouldBe("Queued");
        using IHost worker = await StartWorker();

        await first.Received(count: 20);
        await first.Connection.DisposeAsync();
        var second = new OptimizationProgressClient(await Connect(_user));
        var resumed = await second.Connection.InvokeAsync<JsonElement>("Subscribe", id);
        long resumedAt = resumed.GetProperty("lastSequence").GetInt64();
        long[] gap = await StoredTrials(id, first.Sequences.Max(), resumedAt);
        string resumedStatus = resumed.GetProperty("status").GetString()!;

        // An optimization that ended before the client came back reports its status with the cursor instead of a message.
        string status = resumedStatus is "Completed" or "Failed" or "Cancelled" ? resumedStatus : await second.Finished.WaitAsync(TimeSpan.FromSeconds(30));
        await second.Connection.DisposeAsync();
        await worker.StopAsync();

        first.Sequences.ShouldBe(Enumerable.Range(start: 1, first.Sequences.Count).Select(index => (long)index));
        first.Optimizations.ShouldAllBe(optimization => optimization == id);
        second.Sequences.ShouldBeInOrder(SortDirection.Ascending);
        second.Sequences.ShouldBeUnique();

        // Messages in flight while the client resubscribed may repeat what it reads over HTTP; it keeps each sequence once.
        first.Sequences.Concat(gap)
            .Concat(second.Sequences)
            .Distinct()
            .Order()
            .ShouldBe(Enumerable.Range(start: 1, TrialCount).Select(index => (long)index));
        status.ShouldBe("Completed");
    }

    [Test]
    public async Task AFinishedOptimizationReportsItsStatusOnceAfterTheLastTrial()
    {
        Guid id = await Start();
        var progress = new OptimizationProgressClient(await Connect(_user));
        await progress.Connection.InvokeAsync<JsonElement>("Subscribe", id);
        using IHost worker = await StartWorker();

        string status = await progress.Finished.WaitAsync(TimeSpan.FromSeconds(30));
        await progress.Connection.DisposeAsync();
        await worker.StopAsync();

        status.ShouldBe("Completed");
        progress.Sequences.ShouldBe(Enumerable.Range(start: 1, TrialCount).Select(index => (long)index));
    }

    [Test]
    public async Task AnotherUsersOptimizationCannotBeFollowed()
    {
        Guid id = await Start();
        await using HubConnection other = await Connect(await _application.CreateAccount(UserRole.User));

        var error = await Should.ThrowAsync<HubException>(() => other.InvokeAsync<JsonElement>("Subscribe", id));
        var missing = await Should.ThrowAsync<HubException>(() => other.InvokeAsync<JsonElement>("Subscribe", Guid.NewGuid()));

        error.Message.ShouldContain(OptimizationErrorCodes.NotFound);
        missing.Message.ShouldContain(OptimizationErrorCodes.NotFound);
    }

    private async Task<Guid> Start()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _user.Id);
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/optimizations",
            new
            {
                backtest = BacktestScenario.Request(strategyId, BacktestScenario.NewSymbol()),
                ranges = new Dictionary<string, object>
                {
                    ["fastPeriod"] = new { min = "2", max = "10", step = "1" },
                    ["slowPeriod"] = new { min = "11", max = "30", step = "1" }
                },
                method = "grid",
                seed = "1",
                finalistCount = 0,
                minExecutions = 0
            });

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
    }

    private static async Task<IHost> StartWorker()
    {
        IHost worker = WorkerHost.Create(settings: new Dictionary<string, string?> { ["Optimization:MaxWorkers"] = "1", ["Optimization:BatchSize"] = "1" });
        var history = worker.Services.GetRequiredService<FakeHistorySource>();
        history.Reset();
        history.Candles = BacktestScenario.Wave;
        await worker.StartAsync();

        return worker;
    }

    private async Task<HubConnection> Connect(UserInternal user)
    {
        string cookie = await _application.SessionCookie(user);
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_application.Server.BaseAddress, "/ws/optimizations"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => _application.Server.CreateHandler();
                    options.Headers["Cookie"] = cookie;
                })
            .Build();
        await connection.StartAsync();

        return connection;
    }

    private async Task<long[]> StoredTrials(Guid id, long after, long through)
    {
        var sequences = new List<long>();
        long? cursor = after;

        while (cursor is { } position && position < through)
        {
            var page = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}/trials?after={position}&limit=500"))!;
            sequences.AddRange(page["items"]!.AsArray().Select(item => item!["sequence"]!.GetValue<long>()).Where(sequence => sequence <= through));
            cursor = page["next"]?.GetValue<long>();
        }

        return [.. sequences];
    }
}