using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.GetBotCandles;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.Backtesting;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     The chart of a Steam bot (ADR 0026) against the mock Steam: days without sales have no candle, so the page shows the
///     medians Steam has, marks the history partial instead of failing, and stores nothing of it. The range lies before the
///     hourly zone of the last 30 days, whose first day Steam's history leaves out. Nothing contacts Steam.
/// </summary>
[NonParallelizable]
public sealed class SteamBotCandlesTests
{
    private const string RefreshToken = "refresh-token-not-real";
    private const string Symbol = "KZT:730:Dreams & Nightmares Case";
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Test]
    public async Task DaysWithoutSalesAreLeftOut_AndTheRestIsShownWithoutBeingStored()
    {
        await using var setup = new ProfitGraphApiFactory(BotApi.Settings());
        UserInternal owner = await setup.CreateAccount(UserRole.User);
        var steamId = $"7656119{Random.Shared.NextInt64(minValue: 1_000_000_000, maxValue: 9_999_999_999)}";
        Guid credential = await Import(setup, owner.Login, steamId);
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        int[] quiet = [-40, -39, -35];
        (DateTimeOffset Day, decimal Median, int Sold)[] history =
        [
            .. Enumerable.Range(start: -60, count: 59).Where(day => !quiet.Contains(day)).Select(day => (today.AddDays(day), 500m + day, 10))
        ];
        var steam = new MockSteamCommunity(steamId, RefreshToken, history);
        await using var api = new ProfitGraphApiFactory(
            BotApi.Settings().Concat(MockSteamCommunity.Settings(credential).Select(pair => KeyValuePair.Create(pair.Key, pair.Value!))).ToDictionary(),
            services: services => services.AddHttpClient<SteamHttpTransport>().ConfigurePrimaryHttpMessageHandler(() => steam));
        using HttpClient client = await api.SignIn(owner);
        Guid botId = await NewBot(api, owner, credential);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/bots/{botId}/candles?from={Uri.EscapeDataString(today.AddDays(-44).ToString("O"))}&to={Uri.EscapeDataString(today.AddDays(-31).ToString("O"))}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = (await response.Content.ReadFromJsonAsync<BotCandlesResponse>(WebJson))!;
        page.Venue.ShouldBe(SteamVenue.VenueId.Value);
        page.Timeframe.ShouldBe("1d");
        page.Status.ShouldBe(BotCandleHistoryStatus.Partial);
        page.ErrorCode.ShouldBeNull();
        page.Candles.Select(candle => candle.OpenTime)
            .ShouldBe([.. Enumerable.Range(start: -44, count: 13).Where(day => !quiet.Contains(day)).Select(day => today.AddDays(day))]);
        page.Candles[0].Close.ShouldBe("456");
        steam.HistoryReads.ShouldBeGreaterThan(0);
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM candles WHERE venue = 'steam' AND symbol = @Symbol AND timeframe = '1d' AND open_time >= @From AND open_time < @To",
                new { Symbol, From = today.AddDays(-44), To = today.AddDays(-31) }))
            .ShouldBe(0);
    }

    private static async Task<Guid> Import(ProfitGraphApiFactory api, string owner, string steamId)
    {
        using IServiceScope scope = api.Services.CreateScope();
        IOperatorCommand command = scope.ServiceProvider.GetServices<IOperatorCommand>().Single(candidate => candidate.Name == "steam-import");
        await using var output = new StringWriter();
        var input = $$"""
                      { "owner": "{{owner}}", "label": "market data",
                        "authenticator": { "shared_secret": "c2hhcmVk", "identity_secret": "aWRlbnRpdHktbm90LXJlYWw=", "device_id": "android:0",
                          "Session": { "SteamID": {{steamId}}, "RefreshToken": "{{RefreshToken}}" } } }
                      """;
        (await command.Run(new StringReader(input), output, CancellationToken.None)).ShouldBe(expected: 0, output.ToString());

        return Guid.Parse(output.ToString().Split("credential ")[1].Split(';')[0]);
    }

    /// <summary>
    ///     A stopped daily bot on the case with the owner's imported Steam account.
    /// </summary>
    private static async Task<Guid> NewBot(ProfitGraphApiFactory api, UserInternal owner, Guid credential)
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(api, owner.Id);
        using IServiceScope scope = api.Services.CreateScope();
        StrategyVersion version = (await scope.ServiceProvider.GetRequiredService<IStrategyRepository>()
            .GetVersion(new StrategyId(strategyId), new StrategyVersionNumber(1), CancellationToken.None))!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var bot = Bot.Create(
            BotId.New(),
            new UserId(owner.Id),
            new StrategyId(strategyId),
            version.Id,
            new ExchangeCredentialId(credential),
            new CandleSeries(SteamVenue.VenueId, new InstrumentSymbol(Symbol), Timeframe.Parse("1d")),
            new BotParameters(new Dictionary<string, decimal>()),
            allocationCap: 10000m,
            new BotRiskLimits(maxOrderQuote: 3000m, dailyLossLimitQuote: 5000m),
            new BotFeeRates(limitRate: 0m, marketRate: 0m, now),
            now);
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return bot.Id.Value;
    }
}