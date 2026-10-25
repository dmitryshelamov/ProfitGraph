using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Backtesting;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     Progress over <c>/ws/backtests</c> with the real SignalR client: the Worker's commits reach the API through
///     PostgreSQL <c>LISTEN/NOTIFY</c> (stage 3 plan, S3-9 and S3-11).
/// </summary>
[NonParallelizable]
public sealed class BacktestProgressHubTests
{
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
    public async Task EachConnectionGetsProgressInOrderOnce_AndAReconnectingClientFillsItsGapFromTheCursor()
    {
        Guid id = await BacktestScenario.Start(
            _client,
            BacktestScenario.Request(await BacktestScenario.CreateStrategy(_application, _user.Id), BacktestScenario.NewSymbol()));
        var first = new BacktestProgressClient(await Connect(_user));
        var cursor = await first.Connection.InvokeAsync<JsonElement>("Subscribe", id);
        cursor.GetProperty("lastSequence").GetInt64().ShouldBe(0);
        using IHost worker = await StartWorker();

        await first.Received(count: 20);
        await first.Connection.DisposeAsync();
        var second = new BacktestProgressClient(await Connect(_user));
        var resumed = await second.Connection.InvokeAsync<JsonElement>("Subscribe", id);
        long resumedAt = resumed.GetProperty("lastSequence").GetInt64();
        long[] gap = await Observations(id, first.Sequences.Max(), resumedAt);

        // A run that ended before the client came back reports its outcome with the cursor instead of a message.
        string outcome = resumed.GetProperty("outcome").GetString() ?? await second.Finished.WaitAsync(TimeSpan.FromSeconds(15));
        await second.Connection.DisposeAsync();
        await worker.StopAsync();

        first.Sequences.ShouldBe(Enumerable.Range(start: 1, first.Sequences.Count).Select(index => (long)index));
        first.Runs.ShouldAllBe(run => run == id);
        second.Sequences.ShouldBeInOrder(SortDirection.Ascending);
        second.Sequences.ShouldBeUnique();

        // Messages in flight while the client resubscribed may repeat what it reads over HTTP; it keeps each sequence once.
        first.Sequences.Concat(gap)
            .Concat(second.Sequences)
            .Distinct()
            .Order()
            .ShouldBe(Enumerable.Range(start: 1, count: 240).Select(index => (long)index));
        outcome.ShouldBe("completed");
    }

    [Test]
    public async Task ObservationsCarryTheirFacts()
    {
        Guid id = await BacktestScenario.Start(
            _client,
            BacktestScenario.Request(await BacktestScenario.CreateStrategy(_application, _user.Id), BacktestScenario.NewSymbol()));
        var progress = new BacktestProgressClient(await Connect(_user));
        await progress.Connection.InvokeAsync<JsonElement>("Subscribe", id);
        using IHost worker = await StartWorker();

        await progress.Finished.WaitAsync(TimeSpan.FromSeconds(15));

        // The finish notification may overtake the last progress messages.
        await progress.Received(count: 240);
        await progress.Connection.DisposeAsync();
        await worker.StopAsync();

        progress.Messages.Count.ShouldBe(240);
        JsonElement[] ledger = [.. progress.Messages.SelectMany(message => message.GetProperty("observation").GetProperty("ledger").EnumerateArray())];
        ledger.ShouldNotBeEmpty();
        ledger.Select(entry => entry.GetProperty("sequence").GetInt64()).ShouldBe(Enumerable.Range(start: 1, ledger.Length).Select(index => (long)index));
        progress.Messages[^1].GetProperty("observation").GetProperty("equity").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task AnotherUsersRunCannotBeFollowed()
    {
        Guid id = await BacktestScenario.Start(
            _client,
            BacktestScenario.Request(await BacktestScenario.CreateStrategy(_application, _user.Id), BacktestScenario.NewSymbol()));
        await using HubConnection other = await Connect(await _application.CreateAccount(UserRole.User));

        var error = await Should.ThrowAsync<HubException>(() => other.InvokeAsync<JsonElement>("Subscribe", id));
        var missing = await Should.ThrowAsync<HubException>(() => other.InvokeAsync<JsonElement>("Subscribe", Guid.NewGuid()));

        error.Message.ShouldContain(BacktestErrorCodes.RunNotFound);
        missing.Message.ShouldContain(BacktestErrorCodes.RunNotFound);
    }

    [Test]
    public async Task TheHubRequiresSignIn()
    {
        await using HubConnection anonymous = Build(cookie: null);

        var error = await Should.ThrowAsync<HttpRequestException>(() => anonymous.StartAsync());

        error.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<IHost> StartWorker()
    {
        IHost worker = WorkerHost.Create(settings: new Dictionary<string, string?> { ["Backtests:BatchSize"] = "10", ["Backtests:CompactAfter"] = "1.00:00:00" });
        var history = worker.Services.GetRequiredService<FakeHistorySource>();
        history.Reset();
        history.Candles = BacktestScenario.Wave;
        await worker.StartAsync();

        return worker;
    }

    private async Task<HubConnection> Connect(UserInternal user)
    {
        HubConnection connection = Build(await _application.SessionCookie(user));
        await connection.StartAsync();

        return connection;
    }

    private HubConnection Build(string? cookie)
    {
        return new HubConnectionBuilder()
            .WithUrl(
                new Uri(_application.Server.BaseAddress, "/ws/backtests"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => _application.Server.CreateHandler();

                    if (cookie is not null)
                    {
                        options.Headers["Cookie"] = cookie;
                    }
                })
            .Build();
    }

    private async Task<long[]> Observations(Guid id, long after, long through)
    {
        var sequences = new List<long>();
        long? cursor = after;

        while (cursor is { } position && position < through)
        {
            var page = (await _client.GetFromJsonAsync<JsonNode>($"/api/backtests/{id}/observations?after={position}&limit=100"))!;
            sequences.AddRange(page["items"]!.AsArray().Select(item => item!["sequence"]!.GetValue<long>()).Where(sequence => sequence <= through));
            cursor = page["next"]?.GetValue<long>();
        }

        return [.. sequences];
    }
}