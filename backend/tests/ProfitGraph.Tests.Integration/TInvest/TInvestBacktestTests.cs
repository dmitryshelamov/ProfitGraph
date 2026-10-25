using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Infrastructure;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Tests.Integration.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Unit.Venues.TInvest;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Tests.Integration.TInvest;

/// <summary>
///     The backtest milestone of ADR 0023 through the API and a job Worker with the real T-Invest module against the
///     in-process mock Invest API: a key is added with its account, a MOEX share is found with it, and a Ladder backtest on a
///     week of its minutes runs to its results. Nights and the weekend are closures from the reference share, the share's
///     quiet minutes are candles without trades that fill nothing, every fill is whole lots, and fees are in roubles. The
///     venue names what its backtests do not model. Values are invented; nothing leaves the process.
/// </summary>
[NonParallelizable]
public sealed class TInvestBacktestTests
{
    private static readonly string FakeMasterKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x42, count: 32).ToArray());
    private static readonly DateTimeOffset Monday = new DateTimeOffset(year: 2026, month: 9, day: 7, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private readonly HashSet<DateTimeOffset> _quiet = [];
    private readonly HashSet<DateTimeOffset> _session = [];
    private string _accountId = string.Empty;
    private ProfitGraphApiFactory _api = null!;
    private HttpClient _client = null!;
    private MockTInvestApi _invest = null!;

    [SetUp]
    public async Task StartApi()
    {
        _invest = new MockTInvestApi();
        _accountId = "3" + Random.Shared.NextInt64(minValue: 100_000_000, maxValue: 999_999_999).ToString(CultureInfo.InvariantCulture);
        _ = new MockTInvestOrders(_invest, _accountId);
        AddWeek(new MockTInvestMarket(_invest));
        _api = new ProfitGraphApiFactory(
            Settings().Where(setting => setting.Value is not null).ToDictionary(setting => setting.Key, setting => setting.Value!),
            services: services => services.AddSingleton<ITInvestCallInvokers>(new MockTInvestInvokers(_invest)));
        _client = await _api.CreateSignedInClient();
    }

    [TearDown]
    public async Task StopApi()
    {
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task ALadderBacktestOfAMoexShareRunsFromShareSelectionToItsResults()
    {
        JsonNode venue = (await _client.GetFromJsonAsync<JsonArray>("/api/market-data/venues"))!.Single(item => item!["id"]!.GetValue<string>() == "tinvest")!;
        venue["backtestLimits"]!.AsArray().Select(code => code!.GetValue<string>()).ShouldContain("no_trades_fill_nothing");
        HttpResponseMessage added = await _client.PostAsJsonAsync(
            "/api/exchange-credentials",
            new
            {
                venue = "tinvest", label = "Брокер",
                fields = new Dictionary<string, string> { ["token"] = "t.invented-" + Guid.NewGuid().ToString("N"), ["accountId"] = _accountId }
            });
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        var credentialId = (await added.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
        var found = (await _client.GetFromJsonAsync<JsonArray>($"/api/market-data/instruments?venue=tinvest&query=GA&credentialId={credentialId}"))!;
        var symbol = found.ShouldHaveSingleItem()!["symbol"]!.GetValue<string>();
        symbol.ShouldBe("TQBR:GAZP");
        Guid strategyId = await Strategy();
        using IHost worker = Worker();
        await worker.StartAsync();

        HttpResponseMessage started = await _client.PostAsJsonAsync(
            "/api/backtests",
            new
            {
                strategyId,
                venue = "tinvest",
                symbol,
                timeframe = "1m",
                from = Monday,
                to = Monday.AddDays(7),
                startingCapital = "100000",
                limitFeeRate = "0.0005",
                marketFeeRate = "0.0005",
                outOfSampleFraction = "0.2",
                credentialId
            });
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync());
        var id = (await started.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
        JsonNode job = await JobScenario.WaitFor(_client, id, BacktestScenario.Finished);
        await worker.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());
        HttpResponseMessage read = await _client.GetAsync($"/api/backtests/{id}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var run = (await read.Content.ReadFromJsonAsync<JsonNode>())!;
        run["history"]!["quoteAsset"]!.GetValue<string>().ShouldBe("RUB");
        var replayed = run["history"]!["replayCount"]!.GetValue<int>();
        replayed.ShouldBeLessThan((int)TimeSpan.FromDays(7).TotalMinutes / 2, "nights and the weekend are closures, not candles");
        replayed.ShouldBeGreaterThan(_session.Count / 2);
        JsonArray fills = (await _client.GetFromJsonAsync<JsonNode>($"/api/backtests/{id}/fills"))!["items"]!.AsArray();
        fills.ShouldNotBeEmpty();
        fills.ShouldAllBe(fill => decimal.Parse(fill!["fill"]!["grossBase"]!.GetValue<string>(), CultureInfo.InvariantCulture) % 10m == 0m);
        fills.ShouldAllBe(fill => fill!["fill"]!["feeAsset"]!.GetValue<string>() == "Quote");
        fills.ShouldAllBe(fill => !_quiet.Contains(fill!["candleOpenTime"]!.GetValue<DateTimeOffset>()));
        fills.ShouldAllBe(fill => _session.Contains(fill!["candleOpenTime"]!.GetValue<DateTimeOffset>()));
        var chart = (await _client.GetFromJsonAsync<JsonNode>($"/api/backtests/{id}/chart"))!;
        chart["points"]!.AsArray().ShouldNotBeEmpty();
    }

    /// <summary>
    ///     A week of exchange minutes from 07:00 to 15:00 UTC on weekdays: the reference SBER trades every minute, GAZP drifts
    ///     and swings within each hour and has no trades in every thirteenth minute.
    /// </summary>
    private void AddWeek(MockTInvestMarket market)
    {
        market.AddShare("SBER", lot: 10, increment: 0.01m, Monday.AddYears(-1));
        market.AddShare("GAZP", lot: 10, increment: 0.01m, Monday.AddYears(-1));

        for (var day = 0; day < 5; day++)
        {
            for (DateTimeOffset minute = Monday.AddDays(day).AddHours(7); minute < Monday.AddDays(day).AddHours(15); minute = minute.AddMinutes(1))
            {
                _session.Add(minute);
                market.AddCandle("SBER", minute, close: 300m);
                var index = (int)(minute - Monday).TotalMinutes;

                if (index % 13 == 0)
                {
                    _quiet.Add(minute);

                    continue;
                }

                decimal swing = (index % 60 < 30 ? index % 60 : 60 - index % 60) * 0.1m;
                market.AddCandle("GAZP", minute, 150m - day * 2m + swing);
            }
        }
    }

    private async Task<Guid> Strategy()
    {
        Guid userId = await JobScenario.UserId(_client);
        using IServiceScope scope = _api.Services.CreateScope();
        StrategyInternal created = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(
                new CreateStrategyCommand(
                    userId,
                    "MOEX ladder",
                    LadderStrategyBlockFactory.Create(buyAmount: 3000m, allocationCap: 12_000m, drop: 0.01m, rise: 0.01m).ToInternal(),
                    new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())));

        return created.Id;
    }

    private IHost Worker()
    {
        Dictionary<string, string?> settings = Settings();
        settings["ConnectionStrings:Postgres"] = TestDatabase.ConnectionString;
        settings["BackgroundJobs:PollingInterval"] = TimeSpan.FromMilliseconds(50).ToString();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProfitGraphInfrastructure(builder.Configuration);
        builder.Services.AddBackgroundJobRunner();
        builder.Services.AddTInvestVenue(builder.Configuration);
        builder.Services.AddSingleton<ITInvestCallInvokers>(new MockTInvestInvokers(_invest));

        return builder.Build();
    }

    private static Dictionary<string, string?> Settings()
    {
        return new Dictionary<string, string?>
        {
            ["Secrets:ActiveKeyId"] = "test-v1",
            ["Secrets:MasterKeys:test-v1"] = FakeMasterKey,
            ["Venues:TInvest:Enabled"] = "true",
            ["Venues:TInvest:Endpoint"] = "https://invest.invalid/"
        };
    }
}