using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
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
    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(BotApi.Settings());
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task CreatedBotIsStoppedWithTheVenuesFeeRates()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(BotApi.Path, BotApi.Request(owner));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement bot = await BotApi.Json(response);
        bot.GetProperty("venue").GetString().ShouldBe(TestHistoryVenue.WithHistory);
        bot.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        bot.GetProperty("runtimeStatus").GetString().ShouldBe("Created");
        bot.GetProperty("limitFeeRate").GetString().ShouldBe("0.0008");
        bot.GetProperty("marketFeeRate").GetString().ShouldBe("0.001");
        bot.GetProperty("allocationCap").GetString().ShouldBe("100");
        bot.GetProperty("parameters").GetProperty("buyAmount").GetString().ShouldBe("10");
        _factory.Services.GetRequiredService<FakeFeeRateReader>().LastApiKey.ShouldBe(BotApi.ApiKey);
    }

    [Test]
    public async Task CreatedBotKeepsTheVenuesSellerFees()
    {
        var reader = _factory.Services.GetRequiredService<FakeFeeRateReader>();
        reader.Rates = new VenueFeeRatesInternal(MakerRate: 0m, TakerRate: 0m, new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m));
        BotOwner owner = await BotApi.NewOwner(_factory);

        try
        {
            Guid id = await BotApi.Create(owner);
            JsonElement bot = await BotApi.Json(await owner.Client.GetAsync($"{BotApi.Path}/{id}"));

            bot.GetProperty("limitFeeRate").GetString().ShouldBe("0");
            bot.GetProperty("sellerFees").GetProperty("rates").EnumerateArray().Select(rate => rate.GetString()).ShouldBe(["0.05", "0.1"]);
            bot.GetProperty("sellerFees").GetProperty("minimum").GetString().ShouldBe("0.01");
        }
        finally
        {
            reader.Rates = FakeFeeRateReader.DefaultRates;
        }
    }

    [Test]
    public async Task ABotKeepsItsWalletsIncrementAndStoresNoneOfOneMinorUnit()
    {
        var reader = _factory.Services.GetRequiredService<FakeFeeRateReader>();
        BotOwner owner = await BotApi.NewOwner(_factory);

        try
        {
            reader.Rates = new VenueFeeRatesInternal(MakerRate: 0m, TakerRate: 0m, new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m, increment: 100));
            Guid wholeTenge = await BotApi.Create(owner);
            reader.Rates = new VenueFeeRatesInternal(MakerRate: 0m, TakerRate: 0m, new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m));
            Guid minorUnits = await BotApi.Create(owner);

            using IServiceScope scope = _factory.Services.CreateScope();
            var bots = scope.ServiceProvider.GetRequiredService<IBotRepository>();
            (await bots.Get(new BotId(wholeTenge), CancellationToken.None))!.FeeRates.SellerFees!.Increment.ShouldBe(100);
            (await bots.Get(new BotId(minorUnits), CancellationToken.None))!.FeeRates.SellerFees!.Increment.ShouldBe(1);
            await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
            (await connection.ExecuteScalarAsync<long>("SELECT (seller_fees->>'increment')::bigint FROM bots WHERE id = @id", new { id = wholeTenge })).ShouldBe(100);
            (await connection.ExecuteScalarAsync<bool>("SELECT seller_fees ? 'increment' FROM bots WHERE id = @id", new { id = minorUnits })).ShouldBeFalse();
        }
        finally
        {
            reader.Rates = FakeFeeRateReader.DefaultRates;
        }
    }

    [Test]
    public async Task ABotStoresItsVenuesSettingsWithoutReadingThem()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        JsonElement request = JsonSerializer.SerializeToElement(BotApi.Request(owner));
        var fields = request.Deserialize<Dictionary<string, JsonElement>>()!;
        fields["venueSettings"] = JsonSerializer.SerializeToElement(new { level = "high" });

        Guid defaulted = await BotApi.Create(owner);
        HttpResponseMessage given = await owner.Client.PostAsJsonAsync(BotApi.Path, fields);
        fields["venueSettings"] = JsonSerializer.SerializeToElement(new { level = "max" });
        HttpResponseMessage refused = await owner.Client.PostAsJsonAsync(BotApi.Path, fields);

        given.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement stored = await BotApi.Json(await owner.Client.GetAsync($"{BotApi.Path}/{(await BotApi.Json(given)).GetProperty("id").GetGuid()}"));
        stored.GetProperty("venueSettingsVersion").GetInt32().ShouldBe(3);
        stored.GetProperty("venueSettings").GetProperty("level").GetString().ShouldBe("high");
        JsonElement plain = await BotApi.Json(await owner.Client.GetAsync($"{BotApi.Path}/{defaulted}"));
        plain.GetProperty("venueSettings").GetProperty("level").GetString().ShouldBe("low");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BotApi.Json(refused)).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()).ShouldBe(["VenueSettings.level"]);
    }

    [Test]
    public async Task BotsArePrivateToTheirOwner()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        BotOwner other = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);

        (await other.Client.GetAsync($"{BotApi.Path}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BotApi.Json(await other.Client.GetAsync(BotApi.Path))).GetArrayLength().ShouldBe(0);
        (await BotApi.Json(await owner.Client.GetAsync(BotApi.Path))).EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([id]);
    }

    [Test]
    public async Task AnotherUsersStrategyOrCredentialIsMissing()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        BotOwner other = await BotApi.NewOwner(_factory);

        HttpResponseMessage foreignStrategy = await owner.Client.PostAsJsonAsync(BotApi.Path, BotApi.Request(owner, other.StrategyId));
        HttpResponseMessage foreignCredential = await owner.Client.PostAsJsonAsync(BotApi.Path, BotApi.Request(owner, credentialId: other.CredentialId));

        foreignStrategy.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreignCredential.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BotApi.Code(foreignCredential)).ShouldBe("exchange_credential_not_found");
    }

    [Test]
    public async Task ParametersMustMatchTheLatestVersion()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);

        HttpResponseMessage missing = await owner.Client.PostAsJsonAsync(
            BotApi.Path,
            BotApi.Request(owner, parameters: new Dictionary<string, string> { ["fastPeriod"] = "2" }));
        HttpResponseMessage fractional = await owner.Client.PostAsJsonAsync(
            BotApi.Path,
            BotApi.Request(owner, parameters: new Dictionary<string, string> { ["fastPeriod"] = "2.5", ["slowPeriod"] = "4", ["buyAmount"] = "10" }));
        HttpResponseMessage unknown = await owner.Client.PostAsJsonAsync(
            BotApi.Path,
            BotApi.Request(owner, parameters: new Dictionary<string, string> { ["fastPeriod"] = "2", ["slowPeriod"] = "4", ["buyAmount"] = "10", ["extra"] = "1" }));

        foreach (HttpResponseMessage response in new[] { missing, fractional, unknown })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await BotApi.Json(response)).GetProperty("errors")
                .EnumerateArray()
                .Select(error => error.GetProperty("code").GetString())
                .ShouldContain("parameters_mismatch");
        }
    }

    [Test]
    public async Task UnknownInstrumentIsRefused()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(BotApi.Path, BotApi.Request(owner, symbol: "NOPE"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task LimitsMustBePositiveExactDecimals()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(
            BotApi.Path,
            new
            {
                strategyId = owner.StrategyId, credentialId = owner.CredentialId, symbol = "BTCUSDT", timeframe = "1h",
                parameters = BotApi.Parameters(), allocationCap = "1e2", maxOrderQuote = "0", dailyLossLimitQuote = "10", allowForeignOrders = false
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BotApi.Json(response)).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()).ShouldContain("AllocationCap");
    }

    [Test]
    public async Task VenueWithoutFeeRatesCannotRunBots()
    {
        BotOwner owner = await BotApi.NewOwner(_factory, TestHistoryVenue.WithoutHistory);

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(BotApi.Path, BotApi.Request(owner));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await BotApi.Code(response)).ShouldBe("fee_rates_unavailable");
    }

    [Test]
    public async Task StartAndStopChangeTheDesiredState()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);

        JsonElement started = await BotApi.Json(await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null));
        JsonElement stopped = await BotApi.Json(await owner.Client.PostAsync($"{BotApi.Path}/{id}/stop", content: null));

        started.GetProperty("desiredState").GetString().ShouldBe("Running");
        stopped.GetProperty("desiredState").GetString().ShouldBe("Stopped");
    }

    [Test]
    public async Task StartIsRefusedWhileTradingIsHalted()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await SetHalt(halted: true);

        try
        {
            HttpResponseMessage response = await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await BotApi.Code(response)).ShouldBe("trading_halted");
        }
        finally
        {
            await SetHalt(halted: false);
        }
    }

    [Test]
    public async Task ParametersChangeOnlyOnAStoppedBot()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        var change = new { parameters = new Dictionary<string, string> { ["fastPeriod"] = "3", ["slowPeriod"] = "6", ["buyAmount"] = "12" }, allocationCap = "150" };

        JsonElement changed = await BotApi.Json(await owner.Client.PutAsJsonAsync($"{BotApi.Path}/{id}/parameters", change));
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);
        HttpResponseMessage running = await owner.Client.PutAsJsonAsync($"{BotApi.Path}/{id}/parameters", change);

        changed.GetProperty("allocationCap").GetString().ShouldBe("150");
        changed.GetProperty("parameters").GetProperty("slowPeriod").GetString().ShouldBe("6");
        running.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(running)).ShouldBe("bot_must_be_stopped");
    }

    [Test]
    public async Task RiskLimitsChangeWhileRunning()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);

        JsonElement changed = await BotApi.Json(
            await owner.Client.PutAsJsonAsync($"{BotApi.Path}/{id}/risk-limits", new { maxOrderQuote = "25", dailyLossLimitQuote = "7.5" }));

        changed.GetProperty("maxOrderQuote").GetString().ShouldBe("25");
        changed.GetProperty("dailyLossLimitQuote").GetString().ShouldBe("7.5");
    }

    [Test]
    public async Task ForeignOrdersPolicyChangesOnlyWhenStopped()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);

        JsonElement allowed = await BotApi.Json(await owner.Client.PutAsJsonAsync($"{BotApi.Path}/{id}/foreign-orders", new { allowForeignOrders = true }));
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);
        HttpResponseMessage running = await owner.Client.PutAsJsonAsync($"{BotApi.Path}/{id}/foreign-orders", new { allowForeignOrders = false });

        allowed.GetProperty("allowForeignOrders").GetBoolean().ShouldBeTrue();
        running.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task CredentialOfABotCannotBeDeleted()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        await BotApi.Create(owner);

        HttpResponseMessage response = await owner.Client.DeleteAsync($"/api/exchange-credentials/{owner.CredentialId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(response)).ShouldBe(BotErrorCodes.CredentialInUse);
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
}