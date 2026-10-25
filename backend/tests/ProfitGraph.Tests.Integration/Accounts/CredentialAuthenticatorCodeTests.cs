using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     The current authenticator code is shown only to the Owner, for an own credential of a venue with authenticators
///     (ADR 0022).
/// </summary>
public sealed class CredentialAuthenticatorCodeTests
{
    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new ProfitGraphApiFactory(BotApi.Settings());
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task OnlyTheOwnerAsksAndOnlyAVenueWithAuthenticatorsAnswers()
    {
        HttpClient owner = await _factory.SignIn(await _factory.CreateAccount(UserRole.Owner));
        HttpClient user = await _factory.SignIn(await _factory.CreateAccount(UserRole.User));
        Guid ownCredential = await Credential(owner);
        Guid userCredential = await Credential(user);

        HttpResponseMessage noAuthenticator = await owner.GetAsync($"/api/exchange-credentials/{ownCredential}/authenticator-code");
        HttpResponseMessage notOwner = await user.GetAsync($"/api/exchange-credentials/{userCredential}/authenticator-code");
        HttpResponseMessage foreign = await owner.GetAsync($"/api/exchange-credentials/{userCredential}/authenticator-code");

        noAuthenticator.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await noAuthenticator.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("authenticator_unavailable");
        notOwner.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AVenueWithoutAccountStatusSaysSo()
    {
        HttpClient user = await _factory.SignIn(await _factory.CreateAccount(UserRole.User));
        Guid credential = await Credential(user);

        HttpResponseMessage response = await user.GetAsync($"/api/exchange-credentials/{credential}/venue-status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("account_status_unavailable");
    }

    private static async Task<Guid> Credential(HttpClient client)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/exchange-credentials",
            new { venue = TestHistoryVenue.WithHistory, label = "invented", apiKey = BotApi.ApiKey, apiSecret = "test-api-secret-not-real" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}