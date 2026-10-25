using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Trading.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Tests.Integration.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The bot API on the test venue with invented credentials (stage 5.4). Nothing contacts an exchange: instrument rules
///     and fee rates come from fakes.
/// </summary>
[NonParallelizable]
public sealed class BotEndpointTests
{
    private const string Path = "/api/bots";
    private const string ApiKey = "test-api-key-not-real";

    private static readonly string FakeMasterKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x44, count: 32).ToArray());

    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(
            new Dictionary<string, string>
            {
                ["Secrets:ActiveKeyId"] = "test-v1",
                ["Secrets:MasterKeys:test-v1"] = FakeMasterKey
            });
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task CreatedBotIsStoppedWithTheVenuesFeeRates()
    {
        BotOwner owner = await NewOwner();

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(Path, Request(owner));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement bot = await Json(response);
        bot.GetProperty("venue").GetString().ShouldBe(TestHistoryVenue.WithHistory);
        bot.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        bot.GetProperty("runtimeStatus").GetString().ShouldBe("Created");
        bot.GetProperty("limitFeeRate").GetString().ShouldBe("0.0008");
        bot.GetProperty("marketFeeRate").GetString().ShouldBe("0.001");
        bot.GetProperty("allocationCap").GetString().ShouldBe("100");
        bot.GetProperty("parameters").GetProperty("buyAmount").GetString().ShouldBe("10");
        _factory.Services.GetRequiredService<FakeFeeRateReader>().LastApiKey.ShouldBe(ApiKey);
    }

    [Test]
    public async Task BotsArePrivateToTheirOwner()
    {
        BotOwner owner = await NewOwner();
        BotOwner other = await NewOwner();
        Guid id = await Create(owner);

        (await other.Client.GetAsync($"{Path}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Client.PostAsync($"{Path}/{id}/start", content: null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Json(await other.Client.GetAsync(Path))).GetArrayLength().ShouldBe(0);
        (await Json(await owner.Client.GetAsync(Path))).EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([id]);
    }

    [Test]
    public async Task AnotherUsersStrategyOrCredentialIsMissing()
    {
        BotOwner owner = await NewOwner();
        BotOwner other = await NewOwner();

        HttpResponseMessage foreignStrategy = await owner.Client.PostAsJsonAsync(Path, Request(owner, other.StrategyId));
        HttpResponseMessage foreignCredential = await owner.Client.PostAsJsonAsync(Path, Request(owner, credentialId: other.CredentialId));

        foreignStrategy.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreignCredential.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(foreignCredential)).ShouldBe("exchange_credential_not_found");
    }

    [Test]
    public async Task ParametersMustMatchTheLatestVersion()
    {
        BotOwner owner = await NewOwner();

        HttpResponseMessage missing = await owner.Client.PostAsJsonAsync(Path, Request(owner, parameters: new Dictionary<string, string> { ["fastPeriod"] = "2" }));
        HttpResponseMessage fractional = await owner.Client.PostAsJsonAsync(
            Path,
            Request(owner, parameters: new Dictionary<string, string> { ["fastPeriod"] = "2.5", ["slowPeriod"] = "4", ["buyAmount"] = "10" }));
        HttpResponseMessage unknown = await owner.Client.PostAsJsonAsync(
            Path,
            Request(owner, parameters: new Dictionary<string, string> { ["fastPeriod"] = "2", ["slowPeriod"] = "4", ["buyAmount"] = "10", ["extra"] = "1" }));

        foreach (HttpResponseMessage response in new[] { missing, fractional, unknown })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Json(response)).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("code").GetString()).ShouldContain("parameters_mismatch");
        }
    }

    [Test]
    public async Task UnknownInstrumentIsRefused()
    {
        BotOwner owner = await NewOwner();

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(Path, Request(owner, symbol: "NOPE"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task LimitsMustBePositiveExactDecimals()
    {
        BotOwner owner = await NewOwner();

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(
            Path,
            new
            {
                strategyId = owner.StrategyId, credentialId = owner.CredentialId, symbol = "BTCUSDT", timeframe = "1h",
                parameters = Parameters(), allocationCap = "1e2", maxOrderQuote = "0", dailyLossLimitQuote = "10", allowForeignOrders = false
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()).ShouldContain("AllocationCap");
    }

    [Test]
    public async Task VenueWithoutFeeRatesCannotRunBots()
    {
        BotOwner owner = await NewOwner(venue: TestHistoryVenue.WithoutHistory);

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(Path, Request(owner));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await Code(response)).ShouldBe("fee_rates_unavailable");
    }

    [Test]
    public async Task StartAndStopChangeTheDesiredState()
    {
        BotOwner owner = await NewOwner();
        Guid id = await Create(owner);

        JsonElement started = await Json(await owner.Client.PostAsync($"{Path}/{id}/start", content: null));
        JsonElement stopped = await Json(await owner.Client.PostAsync($"{Path}/{id}/stop", content: null));

        started.GetProperty("desiredState").GetString().ShouldBe("Running");
        stopped.GetProperty("desiredState").GetString().ShouldBe("Stopped");
    }

    [Test]
    public async Task StartIsRefusedWhileTradingIsHalted()
    {
        BotOwner owner = await NewOwner();
        Guid id = await Create(owner);
        await SetHalt(halted: true);

        try
        {
            HttpResponseMessage response = await owner.Client.PostAsync($"{Path}/{id}/start", content: null);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await Code(response)).ShouldBe("trading_halted");
        }
        finally
        {
            await SetHalt(halted: false);
        }
    }

    [Test]
    public async Task KillStopsTheBotUntilItIsStartedAgain()
    {
        BotOwner owner = await NewOwner();
        Guid id = await Create(owner);
        await owner.Client.PostAsync($"{Path}/{id}/start", content: null);

        JsonElement killed = await Json(await owner.Client.PostAsync($"{Path}/{id}/kill", content: null));
        JsonElement restarted = await Json(await owner.Client.PostAsync($"{Path}/{id}/start", content: null));

        killed.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        killed.GetProperty("statusReason").GetString().ShouldBe("Killed");
        restarted.GetProperty("desiredState").GetString().ShouldBe("Running");
        restarted.GetProperty("statusReason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task OnlyTheOwnerHaltsAndTheHaltKillsRunningBots()
    {
        BotOwner owner = await NewOwner();
        Guid id = await Create(owner);
        await owner.Client.PostAsync($"{Path}/{id}/start", content: null);
        HttpClient admin = await _factory.SignIn(await _factory.CreateAccount(UserRole.Owner));

        (await owner.Client.PostAsync("/api/trading-halt/halt", content: null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        try
        {
            DateTimeOffset before = DateTimeOffset.UtcNow;
            var halted = (await (await admin.PostAsync("/api/trading-halt/halt", content: null)).Content.ReadFromJsonAsync<TradingHaltResponse>())!;
            JsonElement seen = await Json(await owner.Client.GetAsync("/api/trading-halt"));
            JsonElement bot = await Json(await owner.Client.GetAsync($"{Path}/{id}"));

            halted.IsHalted.ShouldBeTrue();
            halted.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
            seen.GetProperty("isHalted").GetBoolean().ShouldBeTrue();
            bot.GetProperty("statusReason").GetString().ShouldBe("Killed");
            bot.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        }
        finally
        {
            JsonElement resumed = await Json(await admin.PostAsync("/api/trading-halt/resume", content: null));
            resumed.GetProperty("isHalted").GetBoolean().ShouldBeFalse();
        }
    }

    [Test]
    public async Task ParametersChangeOnlyOnAStoppedBot()
    {
        BotOwner owner = await NewOwner();
        Guid id = await Create(owner);
        var change = new { parameters = new Dictionary<string, string> { ["fastPeriod"] = "3", ["slowPeriod"] = "6", ["buyAmount"] = "12" }, allocationCap = "150" };

        JsonElement changed = await Json(await owner.Client.PutAsJsonAsync($"{Path}/{id}/parameters", change));
        await owner.Client.PostAsync($"{Path}/{id}/start", content: null);
        HttpResponseMessage running = await owner.Client.PutAsJsonAsync($"{Path}/{id}/parameters", change);

        changed.GetProperty("allocationCap").GetString().ShouldBe("150");
        changed.GetProperty("parameters").GetProperty("slowPeriod").GetString().ShouldBe("6");
        running.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Code(running)).ShouldBe("bot_must_be_stopped");
    }

    [Test]
    public async Task RiskLimitsChangeWhileRunning()
    {
        BotOwner owner = await NewOwner();
        Guid id = await Create(owner);
        await owner.Client.PostAsync($"{Path}/{id}/start", content: null);

        JsonElement changed = await Json(await owner.Client.PutAsJsonAsync($"{Path}/{id}/risk-limits", new { maxOrderQuote = "25", dailyLossLimitQuote = "7.5" }));

        changed.GetProperty("maxOrderQuote").GetString().ShouldBe("25");
        changed.GetProperty("dailyLossLimitQuote").GetString().ShouldBe("7.5");
    }

    [Test]
    public async Task ForeignOrdersPolicyChangesOnlyWhenStopped()
    {
        BotOwner owner = await NewOwner();
        Guid id = await Create(owner);

        JsonElement allowed = await Json(await owner.Client.PutAsJsonAsync($"{Path}/{id}/foreign-orders", new { allowForeignOrders = true }));
        await owner.Client.PostAsync($"{Path}/{id}/start", content: null);
        HttpResponseMessage running = await owner.Client.PutAsJsonAsync($"{Path}/{id}/foreign-orders", new { allowForeignOrders = false });

        allowed.GetProperty("allowForeignOrders").GetBoolean().ShouldBeTrue();
        running.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task CredentialOfABotCannotBeDeleted()
    {
        BotOwner owner = await NewOwner();
        await Create(owner);

        HttpResponseMessage response = await owner.Client.DeleteAsync($"/api/exchange-credentials/{owner.CredentialId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Code(response)).ShouldBe(BotErrorCodes.CredentialInUse);
    }

    private async Task<BotOwner> NewOwner(string venue = TestHistoryVenue.WithHistory)
    {
        UserInternal user = await _factory.CreateAccount(UserRole.User);
        HttpClient client = await _factory.SignIn(user);
        Guid strategyId = await BacktestScenario.CreateStrategy(_factory, user.Id);
        HttpResponseMessage credential = await client.PostAsJsonAsync(
            "/api/exchange-credentials",
            new { venue, label = "invented", apiKey = ApiKey, apiSecret = "test-api-secret-not-real" });
        credential.StatusCode.ShouldBe(HttpStatusCode.Created);

        return new BotOwner(client, strategyId, (await Json(credential)).GetProperty("id").GetGuid());
    }

    private static object Request(
        BotOwner owner,
        Guid? strategyId = null,
        Guid? credentialId = null,
        string symbol = "BTCUSDT",
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        return new
        {
            strategyId = strategyId ?? owner.StrategyId,
            credentialId = credentialId ?? owner.CredentialId,
            symbol,
            timeframe = "1h",
            parameters = parameters ?? Parameters(),
            allocationCap = "100",
            maxOrderQuote = "20",
            dailyLossLimitQuote = "10",
            allowForeignOrders = false
        };
    }

    private static Dictionary<string, string> Parameters()
    {
        return new Dictionary<string, string> { ["fastPeriod"] = "2", ["slowPeriod"] = "4", ["buyAmount"] = "10" };
    }

    private static async Task<Guid> Create(BotOwner owner)
    {
        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(Path, Request(owner));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task SetHalt(bool halted)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var halts = scope.ServiceProvider.GetRequiredService<ITradingHaltRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    TradingHalt halt = await halts.GetForUpdate(CancellationToken.None);

                    if (halted)
                    {
                        halt.Halt(DateTimeOffset.UtcNow);
                    }
                    else
                    {
                        halt.Resume(DateTimeOffset.UtcNow);
                    }

                    await halts.Update(halt, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        return (await Json(response)).GetProperty("code").GetString();
    }
}