using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     A venue's enrollment steps through the API, and the credential the last one produces stored once under the
///     enrollment's identifier (ADR 0022).
/// </summary>
public sealed class CredentialEnrollmentEndpointTests
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
    public async Task TheLastStepStoresTheCredentialOnce()
    {
        HttpClient client = await _factory.SignIn(await _factory.CreateAccount(UserRole.User));

        HttpResponseMessage started = await client.PostAsJsonAsync(
            "/api/exchange-credentials/enrollments",
            new { venue = TestHistoryVenue.WithHistory, label = "enrolled", fields = new Dictionary<string, string> { ["login"] = "someone" } });
        var start = await started.Content.ReadFromJsonAsync<JsonElement>();
        Guid id = start.GetProperty("enrollmentId").GetGuid();
        JsonElement wrong = await Step(client, id, "no");
        JsonElement done = await Step(client, id, "yes");
        JsonElement again = await Step(client, id, "yes");
        var credentials = await client.GetFromJsonAsync<JsonElement>("/api/exchange-credentials");

        started.StatusCode.ShouldBe(HttpStatusCode.Created);
        started.Headers.CacheControl!.NoStore.ShouldBeTrue();
        start.GetProperty("step").GetString().ShouldBe("confirm");
        start.GetProperty("oneTimeSecret").GetString().ShouldBe("shown-once");
        wrong.GetProperty("problem").GetString().ShouldBe("invalid_answer");
        done.GetProperty("step").GetString().ShouldBe("completed");
        done.GetProperty("credentialId").GetGuid().ShouldBe(id);
        again.GetProperty("credentialId").GetGuid().ShouldBe(id);
        credentials.EnumerateArray().Count(credential => credential.GetProperty("id").GetGuid() == id).ShouldBe(1);
        _factory.Services.GetRequiredService<FakeCredentialEnrollment>().Stores.ShouldBe(2);
    }

    [Test]
    public async Task AVenueWithoutEnrollmentRefuses()
    {
        HttpClient client = await _factory.SignIn(await _factory.CreateAccount(UserRole.User));

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/exchange-credentials/enrollments",
            new { venue = TestHistoryVenue.WithoutHistory, label = "x", fields = new Dictionary<string, string>() });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("credential_enrollment_unsupported");
    }

    private static async Task<JsonElement> Step(HttpClient client, Guid id, string answer)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/exchange-credentials/enrollments/{id}/steps",
            new { venue = TestHistoryVenue.WithHistory, step = "confirm", fields = new Dictionary<string, string> { ["answer"] = answer } });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}