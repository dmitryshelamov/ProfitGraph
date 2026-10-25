using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Dapper;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Tests.Integration.Backtesting;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     A backtest on Steam median history from start to result, against the mock Steam (ADR 0022, wave 3 backtest milestone):
///     the Worker reads the history through the imported market-data account and replays it with Steam's seller fees and a
///     seven-day hold. Nothing contacts Steam.
/// </summary>
[NonParallelizable]
public sealed class SteamBacktestTests
{
    private const string RefreshToken = "refresh-token-not-real";
    private const string Symbol = "KZT:730:Dreams & Nightmares Case";

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _api = new ProfitGraphApiFactory(BotApi.Settings());
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task ALadderOfMediansRunsWithSteamFeesAndTheHoldTheSameWayTwice()
    {
        UserInternal owner = await _api.CreateAccount(UserRole.User);
        using HttpClient client = await _api.SignIn(owner);
        var steamId = $"7656119{Random.Shared.NextInt64(minValue: 1_000_000_000, maxValue: 9_999_999_999)}";
        Guid credential = await Import(owner.Login, steamId);
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        (DateTimeOffset Day, decimal Median, int Sold)[] history =
        [
            .. Enumerable.Range(start: 0, count: 160).Select(index => (today.AddDays(index - 200), 500m + 60m * (decimal)Math.Sin(index / 4.0), 1000 + index))
        ];
        var steam = new MockSteamCommunity(steamId, RefreshToken, history);
        Guid strategy = await BacktestScenario.CreateStrategy(_api, owner.Id, "Steam SMA");
        var request = new
        {
            strategyId = strategy,
            venue = "steam",
            symbol = Symbol,
            timeframe = "1d",
            from = today.AddDays(-180),
            to = today.AddDays(-45),
            parameters = new Dictionary<string, string> { ["buyAmount"] = "2000" },
            startingCapital = "100000",
            slippage = "0",
            outOfSampleFraction = "0.2",
            sellerFees = new { rates = new[] { "0.05", "0.10" }, minimum = "0.01" },
            settlementHoldSeconds = 604_800
        };

        using (IHost worker = Worker(credential, steam))
        {
            await worker.StartAsync();
            (Guid firstId, JsonNode first) = await Run(client, request);
            (_, JsonNode second) = await Run(client, request);
            await worker.StopAsync();

            first["result"]!["outcome"]!.GetValue<string>().ShouldBe("completed", first.ToJsonString());
            first["configuration"]!["settlementHoldSeconds"]!.GetValue<long>().ShouldBe(604_800);
            first["history"]!["quoteAsset"]!.GetValue<string>().ShouldBe("KZT");
            first["history"]!["rules"]!["quantityStep"]!.GetValue<string>().ShouldBe("1");
            first["result"]!["totalPnl"]!.ToJsonString().ShouldBe(second["result"]!["totalPnl"]!.ToJsonString());
            first["result"]!["metrics"]!["full"]!.ToJsonString().ShouldBe(second["result"]!["metrics"]!["full"]!.ToJsonString());
            steam.HistoryReads.ShouldBeGreaterThan(0);
            await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
            var fills = await connection.QuerySingleAsync<(long Buys, long Sells, decimal SellFees, decimal BuyFees)>(
                sql: """
                     SELECT count(*) FILTER (WHERE snapshot -> 'fill' ->> 'side' = 'Buy'),
                            count(*) FILTER (WHERE snapshot -> 'fill' ->> 'side' = 'Sell'),
                            coalesce(sum((snapshot -> 'fill' ->> 'fee')::numeric) FILTER (WHERE snapshot -> 'fill' ->> 'side' = 'Sell'), 0),
                            coalesce(sum((snapshot -> 'fill' ->> 'fee')::numeric) FILTER (WHERE snapshot -> 'fill' ->> 'side' = 'Buy'), 0)
                     FROM backtest_ledger WHERE run_id = @firstId AND snapshot ->> 'kind' = 'Filled'
                     """,
                new { firstId });
            fills.Buys.ShouldBeGreaterThan(expected: 0, "The strategy must trade for the check to mean anything.");
            fills.Sells.ShouldBeGreaterThan(0);
            fills.SellFees.ShouldBeGreaterThan(0m);
            fills.BuyFees.ShouldBe(0m);
        }
    }

    private static async Task<(Guid Id, JsonNode Run)> Run(HttpClient client, object request)
    {
        Guid id = await BacktestScenario.Start(client, request);
        JsonNode job = await JobScenario.WaitFor(client, id, BacktestScenario.Finished);
        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());

        return (id, (await client.GetFromJsonAsync<JsonNode>($"/api/backtests/{id}"))!);
    }

    private static IHost Worker(Guid credential, MockSteamCommunity steam)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = TestDatabase.ConnectionString,
                ["BackgroundJobs:PollingInterval"] = TimeSpan.FromMilliseconds(50).ToString()
            });
        builder.Configuration.AddInMemoryCollection(BotApi.Settings().ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
        builder.Configuration.AddInMemoryCollection(MockSteamCommunity.Settings(credential));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProfitGraphInfrastructure(builder.Configuration);
        builder.Services.AddBackgroundJobRunner();
        builder.Services.AddBacktestCompaction();
        builder.Services.AddSteamVenue(builder.Configuration);
        builder.Services.AddHttpClient<SteamHttpTransport>().ConfigurePrimaryHttpMessageHandler(() => steam);

        return builder.Build();
    }

    private async Task<Guid> Import(string owner, string steamId)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        IOperatorCommand command = scope.ServiceProvider.GetServices<IOperatorCommand>().Single(candidate => candidate.Name == "steam-import");
        await using var output = new StringWriter();
        var input = $$"""
                      { "owner": "{{owner}}", "label": "market data",
                        "authenticator": { "shared_secret": "c2hhcmVk", "identity_secret": "aWRlbnRpdHk=", "device_id": "android:0",
                          "Session": { "SteamID": {{steamId}}, "RefreshToken": "{{RefreshToken}}" } } }
                      """;

        (await command.Run(new StringReader(input), output, CancellationToken.None)).ShouldBe(expected: 0, output.ToString());

        return Guid.Parse(output.ToString().Split("credential ")[1].Split(';')[0]);
    }
}