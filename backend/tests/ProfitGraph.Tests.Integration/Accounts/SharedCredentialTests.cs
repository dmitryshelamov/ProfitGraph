using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Application.MarketData.StartCandleDownload;
using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;
using ProfitGraph.Tests.Integration.Scanner;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     One credential for two venues of one account (ADR 0061, as Bybit's futures take the spot key): the test venue takes the
///     credentials of a venue that only enrolls keys, so its bots, scanner trades and market data reads use them, while a credential
///     of an unrelated venue is still refused. Values are invented; no venue is contacted.
/// </summary>
[NonParallelizable]
public sealed class SharedCredentialTests
{
    private const string Venue = TestHistoryVenue.WithHistory;
    private const string KeyVenue = "test-shared-key";
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        Dictionary<string, string> settings = BotApi.Settings();
        settings["ScanTrades:Timeframe"] = "1h";
        _api = new ProfitGraphApiFactory(
            settings,
            services: services =>
            {
                services.AddSingleton<IMarketScanSource, TestScanSource>();

                foreach (ServiceDescriptor venue in services.Where(item => item.ImplementationInstance is TestHistoryVenue { Id.Value: Venue }).ToArray())
                {
                    services.Remove(venue);
                }

                services.AddSingleton<IVenueDescriptor>(new TestHistoryVenue(Venue) { CredentialVenue = new VenueId(KeyVenue) });
                services.AddSingleton<IVenueDescriptor>(new TestHistoryVenue(KeyVenue));
                services.AddSingleton<IVenueCredentialSchema>(new TestCredentialSchema(KeyVenue));
            });
        _api.Services.GetRequiredService<FakeHistorySource>().Candles = ScanLevelsTests.Shaped;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        // The database is shared by the run's fixtures; tests that read every venue's snapshot expect none of this venue.
        using (IServiceScope scope = _api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(Venue, DateTimeOffset.UtcNow, [])));
        }

        await _api.DisposeAsync();
    }

    [Test]
    public async Task TheVenuesListNamesTheVenueWhoseCredentialsAVenueTakes()
    {
        BotOwner owner = await BotApi.NewOwner(_api, KeyVenue);

        var venues = (await owner.Client.GetFromJsonAsync<List<JsonElement>>("/api/market-data/venues"))!;

        venues.Single(venue => venue.GetProperty("id").GetString() == Venue).GetProperty("credentialVenue").GetString().ShouldBe(KeyVenue);
        venues.Single(venue => venue.GetProperty("id").GetString() == KeyVenue).GetProperty("credentialVenue").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ABotOnTheVenueTradesWithTheKeyOfTheVenueItNames()
    {
        BotOwner owner = await BotApi.NewOwner(_api, KeyVenue);

        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(BotApi.Path, Request(owner, Venue));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        JsonElement bot = await BotApi.Json(response);
        bot.GetProperty("venue").GetString().ShouldBe(Venue);
        bot.GetProperty("credentialId").GetGuid().ShouldBe(owner.CredentialId);
        _api.Services.GetRequiredService<FakeFeeRateReader>().LastApiKey.ShouldBe(BotApi.ApiKey);
    }

    [Test]
    public async Task ABotWithAnotherVenuesCredentialIsRefused()
    {
        BotOwner unrelated = await BotApi.NewOwner(_api, TestHistoryVenue.WithoutHistory);
        BotOwner reversed = await BotApi.NewOwner(_api);

        HttpResponseMessage foreign = await unrelated.Client.PostAsJsonAsync(BotApi.Path, Request(unrelated, Venue));
        HttpResponseMessage backwards = await reversed.Client.PostAsJsonAsync(BotApi.Path, Request(reversed, KeyVenue));

        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodes(foreign)).ShouldBe([MarketDataCredentials.VenueMismatch]);
        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodes(backwards)).ShouldBe([MarketDataCredentials.VenueMismatch]);
    }

    [Test]
    public async Task ScannerTradesUseTheKeyOfTheVenueItNamesAndTheirBotsTradeOnTheScannedVenue()
    {
        BotOwner owner = await BotApi.NewOwner(_api, KeyVenue);
        HttpResponseMessage saved = await owner.Client.PutAsJsonAsync(
            $"/api/scanner/{Venue}/trade-settings",
            new { credentialId = owner.CredentialId, risk = "5", maxAmount = "1000" });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        await Scan("SHAREDAUSDT");

        var ideas = (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol=SHAREDAUSDT", WebJson))!;
        ScanTradeIdeaResponse levels = ideas.Ideas.First(idea => idea is { Timeframe: "1h", Scenario: "Levels" });
        HttpResponseMessage created = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new
            {
                symbol = "SHAREDAUSDT",
                timeframe = levels.Timeframe,
                scenario = levels.Scenario,
                entry = levels.Entry,
                stop = levels.Stop,
                target = levels.Target
            });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var trade = (await created.Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
        var bot = await owner.Client.GetFromJsonAsync<JsonElement>($"{BotApi.Path}/{trade.BotId}");
        bot.GetProperty("venue").GetString().ShouldBe(Venue);
        bot.GetProperty("credentialId").GetGuid().ShouldBe(owner.CredentialId);
    }

    [Test]
    public async Task ScannerSettingsRefuseAnUnrelatedVenuesCredential()
    {
        BotOwner unrelated = await BotApi.NewOwner(_api, TestHistoryVenue.WithoutHistory);

        HttpResponseMessage refused = await unrelated.Client.PutAsJsonAsync(
            $"/api/scanner/{Venue}/trade-settings",
            new { credentialId = unrelated.CredentialId, risk = "5", maxAmount = "1000" });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodes(refused)).ShouldBe([ScanTradeErrorCodes.WrongVenue]);
    }

    [Test]
    public async Task MarketDataReadsTakeTheKeyOfTheVenueItNames()
    {
        UserInternal user = await _api.CreateAccount(UserRole.User);
        BotOwner owner = await BotApi.NewOwner(_api, KeyVenue, user);
        BotOwner unrelated = await BotApi.NewOwner(_api, TestHistoryVenue.WithoutHistory, user);
        var history = _api.Services.GetRequiredService<FakeHistorySource>();
        int before = history.Accesses.Count;
        DateTimeOffset first = FakeHistorySource.DefaultFirst;
        using IServiceScope scope = _api.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        string symbol = "S" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        await sender.Send(new DownloadCandlesCommand(Venue, symbol, "1h", first, first.AddHours(2), user.Id, owner.CredentialId), CancellationToken.None);

        history.Accesses.Count.ShouldBe(before + 1);
        history.Accesses[^1]!.CredentialId.ShouldBe(owner.CredentialId);
        (await Should.ThrowAsync<ApplicationValidationException>(() =>
                sender.Send(new StartCandleDownloadCommand(user.Id, Venue, symbol, "1h", first, first.AddHours(1), unrelated.CredentialId))))
            .Errors.Single()
            .Code.ShouldBe(MarketDataCredentials.VenueMismatch);
    }

    private static async Task<string?[]> ErrorCodes(HttpResponseMessage response)
    {
        return [.. (await BotApi.Json(response)).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("code").GetString())];
    }

    private static Dictionary<string, object> Request(BotOwner owner, string venue)
    {
        var request = JsonSerializer.SerializeToElement(BotApi.Request(owner)).Deserialize<Dictionary<string, object>>()!;
        request["venue"] = venue;

        return request;
    }

    private async Task Scan(string symbol)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        var row = new ScanSnapshotRowInternal(symbol, new ScanMetrics { Price = 100m }, Incomplete: false);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(Venue, DateTimeOffset.UtcNow, [row])));
    }
}