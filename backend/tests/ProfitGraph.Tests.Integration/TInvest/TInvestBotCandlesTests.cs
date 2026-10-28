using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.CreateExchangeCredential;
using ProfitGraph.Application.Bots.GetBotCandles;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Unit.Venues.TInvest;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Tests.Integration.TInvest;

/// <summary>
///     The chart of a T-Invest bot (ADR 0026) through the real T-Invest module against the in-process mock Invest API: the
///     candles are read with the bot's own token (ADR 0023, TC6), the night is a market closure rather than a hole, and the
///     minutes without trades are candles. The share is this test's own, as candles are a cache shared by every test. The
///     token and account are invented; nothing leaves the process.
/// </summary>
[NonParallelizable]
public sealed class TInvestBotCandlesTests
{
    private static readonly string FakeMasterKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x27, count: 32).ToArray());
    private static readonly DateTimeOffset Monday = new DateTimeOffset(year: 2026, month: 9, day: 7, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private string _accountId = string.Empty;
    private ProfitGraphApiFactory _api = null!;
    private MockTInvestApi _invest = null!;

    [SetUp]
    public void StartApi()
    {
        _invest = new MockTInvestApi();
        _accountId = "3" + Random.Shared.NextInt64(minValue: 100_000_000, maxValue: 999_999_999).ToString(CultureInfo.InvariantCulture);
        _ = new MockTInvestOrders(_invest, _accountId);
        var market = new MockTInvestMarket(_invest);
        market.AddShare("SBER", lot: 10, increment: 0.01m, Monday.AddYears(-1));
        market.AddShare("MGNT", lot: 10, increment: 0.01m, Monday.AddYears(-1));

        for (DateTimeOffset minute = Monday.AddHours(7); minute < Monday.AddHours(15); minute = minute.AddMinutes(1))
        {
            market.AddCandle("SBER", minute, close: 300m);

            if (minute.Minute != 13)
            {
                market.AddCandle("MGNT", minute, close: 150m);
            }
        }

        _api = new ProfitGraphApiFactory(
            new Dictionary<string, string>
            {
                ["Secrets:ActiveKeyId"] = "test-v1",
                ["Secrets:MasterKeys:test-v1"] = FakeMasterKey,
                ["Venues:TInvest:Enabled"] = "true",
                ["Venues:TInvest:Endpoint"] = "https://invest.invalid/"
            },
            services: services => services.AddSingleton<ITInvestCallInvokers>(new MockTInvestInvokers(_invest)));
    }

    [TearDown]
    public async Task StopApi()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task TheChartReadsTheSharesMinutesWithTheBotsToken_AndTheNightIsAClosure()
    {
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using HttpClient client = await _api.SignIn(user);
        string token = "t.invented-" + Guid.NewGuid().ToString("N");
        Guid botId = await NewBot(user, token);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/bots/{botId}/candles?from={Uri.EscapeDataString(Monday.AddHours(6).ToString("O"))}&to={Uri.EscapeDataString(Monday.AddHours(16).ToString("O"))}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = (await response.Content.ReadFromJsonAsync<BotCandlesResponse>(WebJson))!;
        page.Venue.ShouldBe("tinvest");
        page.Timeframe.ShouldBe("1m");
        page.Status.ShouldBe(BotCandleHistoryStatus.Complete, page.ErrorCode);
        page.Candles.Count.ShouldBe(8 * 60);
        page.Candles[0].OpenTime.ShouldBe(Monday.AddHours(7));
        page.Candles[^1].OpenTime.ShouldBe(Monday.AddHours(15).AddMinutes(-1));
        page.Candles.Single(candle => candle.OpenTime == Monday.AddHours(7).AddMinutes(13)).Volume.ShouldBe("0");
        IEnumerable<string?> tokens = _invest.Calls.Where(call => call.Method.EndsWith("GetCandles", StringComparison.Ordinal))
            .Select(call => call.Headers.GetValue("authorization"));
        tokens.ShouldNotBeEmpty();
        tokens.ShouldAllBe(header => header == "Bearer " + token);
    }

    /// <summary>
    ///     A stopped minute bot on MGNT with the user's invented T-Invest key.
    /// </summary>
    private async Task<Guid> NewBot(UserInternal user, string token)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        ExchangeCredentialInternal credential = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(
                new CreateExchangeCredentialCommand(
                    user.Id,
                    "tinvest",
                    "Invented",
                    ApiKey: null,
                    ApiSecret: null,
                    new Dictionary<string, string> { [TInvestCredentialFields.Token] = token, [TInvestCredentialFields.AccountId] = _accountId }));
        Guid strategyId = await BacktestScenario.CreateStrategy(_api, user.Id);
        StrategyVersion version = (await scope.ServiceProvider.GetRequiredService<IStrategyRepository>()
            .GetVersion(new StrategyId(strategyId), new StrategyVersionNumber(1), CancellationToken.None))!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var bot = Bot.Create(
            BotId.New(),
            new UserId(user.Id),
            new StrategyId(strategyId),
            version.Id,
            new ExchangeCredentialId(credential.Id),
            new CandleSeries(TInvestVenue.VenueId, new InstrumentSymbol("TQBR:MGNT"), Timeframe.Parse("1m")),
            new BotParameters(new Dictionary<string, decimal>()),
            allocationCap: 10000m,
            new BotRiskLimits(maxOrderQuote: 3000m, dailyLossLimitQuote: 5000m),
            new BotFeeRates(limitRate: 0.0005m, marketRate: 0.0005m, now),
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