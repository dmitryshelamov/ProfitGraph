using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     A credential that selects a venue account (ADR 0023, TC5): the form lists the accounts, saving lists them again and
///     takes only an eligible one, and one venue account is stored once. Values are invented; no venue is contacted.
/// </summary>
[NonParallelizable]
public sealed class VenueAccountCredentialTests
{
    private const string Secret = "s-invented-not-real";
    private static readonly string FakeMasterKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x44, count: 32).ToArray());
    private ProfitGraphApiFactory _factory = null!;
    private FakeAccountDirectory _directory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(new Dictionary<string, string> { ["Secrets:ActiveKeyId"] = "test-v1", ["Secrets:MasterKeys:test-v1"] = FakeMasterKey });
        _directory = _factory.Services.GetRequiredService<FakeAccountDirectory>();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [SetUp]
    public void ResetDirectory()
    {
        _directory.Reset();
    }

    [Test]
    public async Task TheFormListsTheAccounts_WithoutEchoingTheSecret()
    {
        using HttpClient client = await _factory.CreateSignedInClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/exchange-credentials/accounts", Listing(Secret));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(Secret);
        JsonArray accounts = JsonNode.Parse(body)!.AsArray();
        accounts.Select(account => account!["id"]!.GetValue<string>()).ShouldBe(["1000123456", "2000654321"]);
        accounts.Select(account => account!["eligible"]!.GetValue<bool>()).ShouldBe([true, false]);
        accounts[1]!["ineligibility"]!.GetValue<string>().ShouldBe("not_full_access");
    }

    [TestCase("test-accounts", "{\"secret\":\"x\",\"accountId\":\"1\"}", "credential_fields_mismatch")]
    [TestCase("test-history", "{\"apiKey\":\"x\"}", "credential_accounts_unsupported")]
    [TestCase("unknown", "{}", "unknown_venue")]
    public async Task AListingNeedsAVenueWithAccountsAndItsFieldsButTheAccount(string venue, string fields, string code)
    {
        using HttpClient client = await _factory.CreateSignedInClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/exchange-credentials/accounts", new { venue, fields = JsonNode.Parse(fields) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(code);
    }

    [TestCase("refused", HttpStatusCode.BadRequest, "invalid_venue_credential")]
    [TestCase("down", HttpStatusCode.ServiceUnavailable, "venue_accounts_unavailable")]
    public async Task TheVenuesRefusalOrUnavailabilityIsReported(string secret, HttpStatusCode status, string code)
    {
        using HttpClient client = await _factory.CreateSignedInClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/exchange-credentials/accounts", Listing(secret));

        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadAsStringAsync()).ShouldContain(code);
    }

    [Test]
    public async Task SavingListsTheAccountsAgain_AndStoresTheEligibleOneOnce()
    {
        using HttpClient client = await _factory.CreateSignedInClient();
        using HttpClient other = await _factory.CreateSignedInClient();

        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/exchange-credentials", Credential("1000123456"));
        using HttpResponseMessage again = await other.PostAsJsonAsync("/api/exchange-credentials", Credential("1000123456"));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        string body = await created.Content.ReadAsStringAsync();
        body.ShouldNotContain(Secret);
        JsonNode.Parse(body)!["apiKeyLast4"]!.GetValue<string>().ShouldBe("3456");
        _directory.Calls.ShouldBe(2);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).ShouldContain("exchange_credential_account_exists");
    }

    [Test]
    public async Task AnIneligibleOrMeanwhileRevokedAccount_IsNotSaved()
    {
        using HttpClient client = await _factory.CreateSignedInClient();
        _directory.Accounts = [.. _directory.Accounts, new VenueAccountInternal("1000999999", "Closed", "broker", Eligible: false, "not_open")];

        using HttpResponseMessage readOnly = await client.PostAsJsonAsync("/api/exchange-credentials", Credential("2000654321"));
        using HttpResponseMessage revoked = await client.PostAsJsonAsync("/api/exchange-credentials", Credential("1000999999"));
        using HttpResponseMessage unknown = await client.PostAsJsonAsync("/api/exchange-credentials", Credential("3000000000"));

        readOnly.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        revoked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await revoked.Content.ReadAsStringAsync()).ShouldContain("venue_account_ineligible");
    }

    [Test]
    public async Task AVenueWithAccountsTakesItsFieldsOnly()
    {
        using HttpClient client = await _factory.CreateSignedInClient();

        using HttpResponseMessage keyAndSecret = await client.PostAsJsonAsync(
            "/api/exchange-credentials",
            new { venue = TestHistoryVenue.WithAccounts, label = "Broker", apiKey = "key-1234", apiSecret = Secret });

        keyAndSecret.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await keyAndSecret.Content.ReadAsStringAsync()).ShouldContain("credential_fields_mismatch");
    }

    private static object Listing(string secret)
    {
        return new { venue = TestHistoryVenue.WithAccounts, fields = new Dictionary<string, string> { [TestAccountCredentialSchema.Secret] = secret } };
    }

    private static object Credential(string account)
    {
        return new
        {
            venue = TestHistoryVenue.WithAccounts,
            label = "Broker",
            fields = new Dictionary<string, string> { [TestAccountCredentialSchema.Secret] = Secret, [TestAccountCredentialSchema.Account] = account }
        };
    }
}