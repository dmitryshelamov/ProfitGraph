using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Instruments;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     Instrument lists and captured rules of the test venue (ADR 0010, Profit Fox Designer ADR 0021).
/// </summary>
[NonParallelizable]
public sealed class InstrumentTests
{
    private ProfitGraphApiFactory _application = null!;
    private FakeInstrumentSource _instruments = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        // Sub-microsecond ticks, which the database cannot keep, as Linux clocks produce them.
        _application = new ProfitGraphApiFactory(new TestClock { Now = new DateTimeOffset(ticks: 639_000_000_000_000_007, TimeSpan.Zero) });
        _instruments = _application.Services.GetRequiredService<FakeInstrumentSource>();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _application.DisposeAsync();
    }

    [SetUp]
    public void ResetVenue()
    {
        _instruments.Reset();
    }

    [Test]
    public async Task CapturedRulesAreStoredAsAnImmutableSnapshot()
    {
        string symbol = UniqueSymbol();

        InstrumentRulesSnapshotInternal captured = await Capture(TestHistoryVenue.WithHistory, symbol);
        InstrumentRulesSnapshotInternal? stored = await Stored(captured.Id);

        stored.ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.Id.ShouldBe(captured.Id),
            () => stored.CapturedAt.ShouldBe(captured.CapturedAt),
            () => stored.Definition.ShouldBe(captured.Definition),
            () => stored.Definition.Rules.ShouldBe(FakeInstrumentSource.DefaultRules));
    }

    [Test]
    public async Task EachCaptureIsANewSnapshot()
    {
        string symbol = UniqueSymbol();

        InstrumentRulesSnapshotInternal first = await Capture(TestHistoryVenue.WithHistory, symbol);
        InstrumentRulesSnapshotInternal second = await Capture(TestHistoryVenue.WithHistory, symbol);

        second.Id.ShouldNotBe(first.Id);
        (await Stored(first.Id)).ShouldNotBeNull();
    }

    [Test]
    public async Task RulesOfAnotherInstrument_AreRejected()
    {
        string symbol = UniqueSymbol();
        _instruments.Rules = _ => FakeInstrumentSource.Definition("OTHERUSDT");

        var error = await Should.ThrowAsync<InstrumentRulesException>(() => Capture(TestHistoryVenue.WithHistory, symbol));

        error.Code.ShouldBe(InstrumentRulesErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task VenueWithoutRules_IsAnInvalidRequest()
    {
        var error = await Should.ThrowAsync<InstrumentRulesException>(() => Capture(TestHistoryVenue.WithoutHistory, UniqueSymbol()));

        error.Code.ShouldBe(InstrumentRulesErrorCodes.InvalidRequest);
    }

    [Test]
    public async Task UnknownSnapshot_IsMissing()
    {
        (await Stored(Guid.NewGuid())).ShouldBeNull();
    }

    [Test]
    public async Task InstrumentsEndpoint_SearchesTheVenueCatalog()
    {
        _instruments.Instruments =
        [
            new InstrumentInternal(TestHistoryVenue.WithHistory, "BTCUSDT", "BTC", "USDT"),
            new InstrumentInternal(TestHistoryVenue.WithHistory, "ETHUSDT", "ETH", "USDT")
        ];
        using HttpClient client = await _application.CreateSignedInClient();

        var found = (await client.GetFromJsonAsync<JsonArray>($"/api/market-data/instruments?venue={TestHistoryVenue.WithHistory}&query=eth"))!;

        found.Count.ShouldBe(1);
        found[0]!["venue"]!.GetValue<string>().ShouldBe(TestHistoryVenue.WithHistory);
        found[0]!["symbol"]!.GetValue<string>().ShouldBe("ETHUSDT");
        found[0]!["baseAsset"]!.GetValue<string>().ShouldBe("ETH");
        found[0]!["quoteAsset"]!.GetValue<string>().ShouldBe("USDT");
        (await client.GetFromJsonAsync<JsonArray>($"/api/market-data/instruments?venue={TestHistoryVenue.WithHistory}&limit=1"))!.Count.ShouldBe(1);
    }

    [TestCase("venue=missing", "unknown_venue")]
    [TestCase("venue=" + TestHistoryVenue.WithoutHistory, InstrumentRulesErrorCodes.InvalidRequest)]
    [TestCase("venue=" + TestHistoryVenue.WithHistory + "&limit=0", "InclusiveBetweenValidator")]
    [TestCase("venue=" + TestHistoryVenue.WithHistory + "&limit=101", "InclusiveBetweenValidator")]
    public async Task InstrumentsEndpoint_RejectsInvalidRequests(string query, string code)
    {
        using HttpClient client = await _application.CreateSignedInClient();

        HttpResponseMessage response = await client.GetAsync($"/api/market-data/instruments?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        problem["code"]!.GetValue<string>().ShouldBe("validation_failed");
        problem["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).ShouldContain(code);
    }

    [Test]
    public async Task InstrumentsEndpoint_ReportsAnUnavailableVenueAs503()
    {
        using HttpClient client = await _application.CreateSignedInClient();
        _instruments.SearchFailure = new InstrumentRulesException(InstrumentRulesErrorCodes.ProviderUnavailable, "The venue is down.");

        HttpResponseMessage response = await client.GetAsync($"/api/market-data/instruments?venue={TestHistoryVenue.WithHistory}");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonNode>())!["code"]!.GetValue<string>().ShouldBe(InstrumentRulesErrorCodes.ProviderUnavailable);
    }

    [Test]
    public async Task InstrumentsEndpoint_RequiresSignIn()
    {
        using HttpClient anonymous = _application.CreateClient();

        (await anonymous.GetAsync($"/api/market-data/instruments?venue={TestHistoryVenue.WithHistory}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<InstrumentRulesSnapshotInternal> Capture(string venue, string symbol)
    {
        using IServiceScope scope = _application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<InstrumentRulesCaptureService>()
            .Capture(new VenueId(venue), new InstrumentSymbol(symbol), CancellationToken.None);
    }

    private async Task<InstrumentRulesSnapshotInternal?> Stored(Guid id)
    {
        using IServiceScope scope = _application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IInstrumentRulesRepository>().Get(id, CancellationToken.None);
    }

    private static string UniqueSymbol()
    {
        return "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "USDT";
    }
}