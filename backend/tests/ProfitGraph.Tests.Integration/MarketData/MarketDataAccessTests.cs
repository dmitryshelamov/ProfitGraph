using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Application.MarketData.StartCandleDownload;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     Market data read with the caller's credential (ADR 0023, TC6): only the user's own credential of the series' venue,
///     handed to the venue for the reads of that use case only. Values are invented; no venue is contacted.
/// </summary>
[NonParallelizable]
public sealed class MarketDataAccessTests
{
    private const string ApiKey = "market-data-key-invented";
    private static readonly string FakeMasterKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x55, count: 32).ToArray());
    private static readonly DateTimeOffset First = FakeHistorySource.DefaultFirst;
    private ProfitGraphApiFactory _factory = null!;
    private FakeHistorySource _history = null!;
    private string _symbol = string.Empty;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(new Dictionary<string, string> { ["Secrets:ActiveKeyId"] = "test-v1", ["Secrets:MasterKeys:test-v1"] = FakeMasterKey });
        _history = _factory.Services.GetRequiredService<FakeHistorySource>();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [SetUp]
    public void ResetVenue()
    {
        _history.Reset();
        _symbol = "A" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    [Test]
    public async Task ADownloadWithTheUsersCredential_HandsItToTheVenue_AndOneWithoutHandsNone()
    {
        (Guid userId, Guid credentialId, _) = await UserWithCredential(TestHistoryVenue.WithHistory);

        await Download(userId, credentialId, First, First.AddHours(2));
        await Download(userId, credentialId: null, First.AddHours(2), First.AddHours(4));

        _history.Accesses.Count.ShouldBe(2);
        _history.Accesses[0]!.CredentialId.ShouldBe(credentialId);
        _history.Accesses[0]!.ApiKey.ShouldBe(ApiKey);
        _history.Accesses[1].ShouldBeNull();
        MarketDataAccess.Current.ShouldBeNull();
    }

    [Test]
    public async Task OnlyTheUsersOwnCredentialOfTheSeriesVenueIsAccepted()
    {
        (_, Guid foreign, _) = await UserWithCredential(TestHistoryVenue.WithHistory);
        (Guid userId, Guid otherVenue, _) = await UserWithCredential(TestHistoryVenue.WithoutHistory);
        using IServiceScope scope = _factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        (await Should.ThrowAsync<ResourceNotFoundException>(() => sender.Send(Start(userId, foreign)))).Code.ShouldBe("exchange_credential_not_found");
        (await Should.ThrowAsync<ApplicationValidationException>(() => sender.Send(Start(userId, otherVenue))))
            .Errors.Single()
            .Code.ShouldBe(MarketDataCredentials.VenueMismatch);
    }

    [Test]
    public async Task AJobWhoseCredentialWasDeleted_RunsWithoutAccess()
    {
        (Guid userId, Guid credentialId, HttpClient client) = await UserWithCredential(TestHistoryVenue.WithHistory);
        (await client.DeleteAsync($"/api/exchange-credentials/{credentialId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await Download(userId, credentialId, First, First.AddHours(2));

        _history.Accesses.ShouldBe([null]);
    }

    [Test]
    public async Task ASearchWithTheUsersCredential_HandsItToTheCatalog()
    {
        (_, Guid credentialId, HttpClient client) = await UserWithCredential(TestHistoryVenue.WithHistory);
        var catalog = _factory.Services.GetRequiredService<FakeInstrumentSource>();

        using HttpResponseMessage response =
            await client.GetAsync($"/api/market-data/instruments?venue={TestHistoryVenue.WithHistory}&query=&credentialId={credentialId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        catalog.SearchAccess!.CredentialId.ShouldBe(credentialId);
    }

    private static StartCandleDownloadCommand Start(Guid userId, Guid credentialId)
    {
        return new StartCandleDownloadCommand(userId, TestHistoryVenue.WithHistory, "ANYSYMBOL", "1h", First, First.AddHours(1), credentialId);
    }

    private async Task Download(Guid userId, Guid? credentialId, DateTimeOffset from, DateTimeOffset to)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new DownloadCandlesCommand(TestHistoryVenue.WithHistory, _symbol, "1h", from, to, userId, credentialId), CancellationToken.None);
    }

    private async Task<(Guid UserId, Guid CredentialId, HttpClient Client)> UserWithCredential(string venue)
    {
        UserInternal user = await _factory.CreateAccount(UserRole.User);
        HttpClient client = await _factory.SignIn(user);
        using HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/exchange-credentials",
            new { venue, label = "Data", apiKey = ApiKey, apiSecret = "market-data-secret-invented" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (user.Id, Guid.Parse(JsonNode.Parse(await created.Content.ReadAsStringAsync())!["id"]!.GetValue<string>()), client);
    }
}