using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     Credentials are invented values; no exchange is contacted. One fixture replaces the console, so it runs alone.
/// </summary>
[NonParallelizable]
public sealed class ExchangeCredentialEndpointTests
{
    private const string ApiKey = "test-api-key-not-real";
    private const string ApiSecret = "test-api-secret-not-real";
    private const string Path = "/api/exchange-credentials";

    private static readonly string FakeMasterKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x33, count: 32).ToArray());

    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = WithMasterKey();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task CredentialsArePrivateAndStoredEncrypted()
    {
        UserInternal first = await _factory.CreateAccount(UserRole.User);
        using HttpClient firstClient = await _factory.SignIn(first);
        using HttpClient secondClient = await _factory.CreateSignedInClient();

        HttpResponseMessage created = await firstClient.PostAsJsonAsync(Path, Request());

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        string createdBody = await created.Content.ReadAsStringAsync();
        createdBody.ShouldNotContain(ApiSecret);
        createdBody.ShouldNotContain(ApiKey);
        JsonElement credential = JsonDocument.Parse(createdBody).RootElement;
        Guid id = credential.GetProperty("id").GetGuid();
        credential.GetProperty("venue").GetString().ShouldBe("bybit");
        credential.GetProperty("label").GetString().ShouldBe("main key");
        credential.GetProperty("apiKeyLast4").GetString().ShouldBe("real");

        string own = await firstClient.GetStringAsync(Path);
        JsonDocument.Parse(own).RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([id]);
        own.ShouldNotContain(ApiSecret);
        JsonDocument.Parse(await secondClient.GetStringAsync(Path)).RootElement.GetArrayLength().ShouldBe(0);

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        var row = await connection.QuerySingleAsync<(Guid UserId, string Secrets, byte[] Key, byte[] Secret)>(
            "SELECT user_id, secrets::text, decode(secrets -> 'apiKey' ->> 'ciphertext', 'base64'), " +
            "decode(secrets -> 'apiSecret' ->> 'ciphertext', 'base64') FROM exchange_credentials WHERE id = @id",
            new { id });
        row.UserId.ShouldBe(first.Id);
        row.Secrets.ShouldNotContain(ApiKey);
        row.Secrets.ShouldNotContain(ApiSecret);
        Encoding.UTF8.GetString(row.Key).ShouldNotContain(ApiKey);
        Encoding.UTF8.GetString(row.Secret).ShouldNotContain(ApiSecret);
    }

    [Test]
    public async Task OnlyTheOwnerReadsTheDecryptedSecrets()
    {
        UserInternal owner = await _factory.CreateAccount(UserRole.User);
        using HttpClient client = await _factory.SignIn(owner);
        Guid id = await Create(client);
        using IServiceScope scope = _factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        ExchangeCredentialSecretsInternal secrets = await sender.Send(new ReadExchangeCredentialSecretsQuery(owner.Id, id));

        secrets.ApiKey.ShouldBe(ApiKey);
        secrets.ApiSecret.ShouldBe(ApiSecret);
        secrets.ToString().ShouldNotContain(ApiKey);
        secrets.ToString().ShouldNotContain(ApiSecret);
        (await Should.ThrowAsync<ResourceNotFoundException>(() => sender.Send(new ReadExchangeCredentialSecretsQuery(Guid.NewGuid(), id))))
            .Code.ShouldBe("exchange_credential_not_found");
    }

    [Test]
    public async Task OnlyTheOwnerDeletesACredential()
    {
        using HttpClient owner = await _factory.CreateSignedInClient();
        using HttpClient other = await _factory.CreateSignedInClient();
        Guid id = await Create(owner);

        HttpResponseMessage foreign = await other.DeleteAsync($"{Path}/{id}");

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(foreign)).ShouldBe("exchange_credential_not_found");
        JsonDocument.Parse(await owner.GetStringAsync(Path)).RootElement.GetArrayLength().ShouldBe(1);
        (await owner.DeleteAsync($"{Path}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonDocument.Parse(await owner.GetStringAsync(Path)).RootElement.GetArrayLength().ShouldBe(0);
        (await owner.DeleteAsync($"{Path}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestCase("unknown", "main", ApiKey, ApiSecret, "unknown_venue")]
    [TestCase(null, "main", ApiKey, ApiSecret, "unknown_venue")]
    [TestCase("bybit", " ", ApiKey, ApiSecret, "invalid_credential_label")]
    [TestCase("bybit", "main", "abc", ApiSecret, "invalid_api_key")]
    [TestCase("bybit", "main", "key with spaces", ApiSecret, "invalid_api_key")]
    [TestCase("bybit", "main", ApiKey, "", "invalid_api_secret")]
    [TestCase("steam", "main", ApiKey, ApiSecret, "credential_input_unsupported")]
    public async Task InvalidInputIsRejectedWithoutEchoingIt(string? venue, string label, string apiKey, string apiSecret, string code)
    {
        using HttpClient client = await _factory.CreateSignedInClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(Path, new { venue, label, apiKey, apiSecret });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("code").GetString()).ShouldContain(code);
        body.ShouldNotContain(ApiSecret);
        body.ShouldNotContain(ApiKey);
    }

    [Test]
    public async Task AnonymousRequestsAreRejected()
    {
        using HttpClient anonymous = _factory.CreateClient();

        (await anonymous.GetAsync(Path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(Path, Request())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task EveryEndpointAnswers503WithoutAMasterKey()
    {
        await using var factory = new ProfitGraphApiFactory();
        using HttpClient client = await factory.CreateSignedInClient();

        HttpResponseMessage[] responses =
        [
            await client.GetAsync(Path),
            await client.PostAsJsonAsync(Path, Request()),
            await client.DeleteAsync($"{Path}/{Guid.NewGuid()}")
        ];

        foreach (HttpResponseMessage response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await Code(response)).ShouldBe("credentials_unavailable");
        }
    }

    [Test]
    public async Task CreatingACredentialWritesNoKeyOrSecretToTheLog()
    {
        TextWriter original = Console.Out;
        await using var captured = new StringWriter();

        // Hosts of other fixtures may still write while this one reads, so every access goes through one lock.
        TextWriter synchronized = TextWriter.Synchronized(captured);

        try
        {
            Console.SetOut(synchronized);
            await using ProfitGraphApiFactory factory = WithMasterKey(verboseLogs: true);
            using HttpClient client = await factory.CreateSignedInClient();
            (await client.PostAsJsonAsync(Path, Request())).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await client.PostAsJsonAsync(Path, new { venue = "bybit", label = "main", apiKey = ApiKey, apiSecret = $"{ApiSecret} rejected" }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        finally
        {
            Console.SetOut(original);
        }

        string log;

        lock (synchronized)
        {
            log = captured.ToString();
        }

        log.ShouldContain("ExchangeCredentialsController");
        log.Contains(ApiKey, StringComparison.Ordinal).ShouldBeFalse();
        log.Contains(ApiSecret, StringComparison.Ordinal).ShouldBeFalse();
    }

    /// <summary>
    ///     Verbose logs include framework diagnostics such as endpoint selection and model binding, the most detailed
    ///     place a request value could leak to.
    /// </summary>
    private static ProfitGraphApiFactory WithMasterKey(bool verboseLogs = false)
    {
        var settings = new Dictionary<string, string>
        {
            ["Secrets:ActiveKeyId"] = "test-v1",
            ["Secrets:MasterKeys:test-v1"] = FakeMasterKey
        };

        if (verboseLogs)
        {
            settings["Serilog:MinimumLevel:Default"] = "Verbose";
            settings["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose";
        }

        return new ProfitGraphApiFactory(settings);
    }

    private static object Request()
    {
        return new { venue = "bybit", label = " main key ", apiKey = ApiKey, apiSecret = ApiSecret };
    }

    private static async Task<Guid> Create(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(Path, Request());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    }
}