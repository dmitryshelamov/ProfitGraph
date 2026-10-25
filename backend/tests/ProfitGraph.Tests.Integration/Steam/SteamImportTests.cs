using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     The console import of an existing Steam Guard with invented values (ADR 0022). Nothing contacts Steam.
/// </summary>
[NonParallelizable]
public sealed class SteamImportTests
{
    private const string RefreshToken = "refresh-token-not-real";
    private const string SharedSecret = "c2hhcmVkLXNlY3JldC1ub3QtcmVhbA==";

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
    public async Task ImportStoresTheAccountOnceAndBeingChecked()
    {
        UserInternal owner = await _factory.CreateAccount(UserRole.User);
        var steamId = $"7656119{Random.Shared.NextInt64(minValue: 1_000_000_000, maxValue: 9_999_999_999)}";

        (int code, string output) = await Import(Input(owner.Login, steamId));

        code.ShouldBe(expected: 0, output);
        output.ShouldContain($"ending {steamId[^4..]}");
        output.ShouldNotContain(RefreshToken);
        output.ShouldNotContain(SharedSecret);
        using HttpClient client = await _factory.SignIn(owner);
        JsonNode credential = (await client.GetFromJsonAsync<JsonArray>("/api/exchange-credentials"))!.Single()!;
        credential["venue"]!.GetValue<string>().ShouldBe("steam");
        credential["apiKeyLast4"]!.GetValue<string>().ShouldBe(steamId[^4..]);
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        var account = await connection.QuerySingleAsync<(string GuardStatus, string Fingerprint, string Secrets)>(
            "SELECT a.guard_status, a.account_fingerprint, c.secrets::text FROM steam_accounts a JOIN exchange_credentials c ON c.id = a.credential_id WHERE c.id = @id",
            new { id = credential["id"]!.GetValue<Guid>() });
        account.GuardStatus.ShouldBe("Checking");
        account.Fingerprint.ShouldNotContain(steamId);
        account.Secrets.ShouldNotContain(steamId);
        account.Secrets.ShouldNotContain(RefreshToken);

        (int again, string refused) = await Import(Input(owner.Login, steamId));

        again.ShouldBe(1);
        refused.ShouldContain("already stored");
    }

    [Test]
    public async Task TheOwnerReadsAndResumesTheAccountsProtection()
    {
        UserInternal owner = await _factory.CreateAccount(UserRole.User);
        UserInternal other = await _factory.CreateAccount(UserRole.User);
        var steamId = $"7656119{Random.Shared.NextInt64(minValue: 1_000_000_000, maxValue: 9_999_999_999)}";
        (await Import(Input(owner.Login, steamId))).Code.ShouldBe(0);
        using HttpClient client = await _factory.SignIn(owner);
        using HttpClient stranger = await _factory.SignIn(other);
        var id = (await client.GetFromJsonAsync<JsonArray>("/api/exchange-credentials"))!.Single()!["id"]!.GetValue<Guid>();
        var path = $"/api/exchange-credentials/{id}/protection";

        (await client.GetFromJsonAsync<JsonNode>(path))!["open"]!.GetValue<bool>().ShouldBeFalse();

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync("INSERT INTO steam_account_protection (credential_id, reason, opened_at) VALUES (@id, 'Captcha', now())", new { id });
        }

        var open = (await client.GetFromJsonAsync<JsonNode>(path))!;
        open["open"]!.GetValue<bool>().ShouldBeTrue();
        open["reason"]!.GetValue<string>().ShouldBe("Captcha");
        open["automaticResumeAt"].ShouldBeNull();
        (await stranger.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"{path}/resume", content: null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await client.PostAsync($"{path}/resume", content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonNode>(path))!["open"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Test]
    public async Task WithSteamDisabledTheGuardCodeIsUnavailableNotAServerError()
    {
        UserInternal owner = await _factory.CreateAccount(UserRole.Owner);
        var steamId = $"7656119{Random.Shared.NextInt64(minValue: 1_000_000_000, maxValue: 9_999_999_999)}";
        (await Import(Input(owner.Login, steamId))).Code.ShouldBe(0);
        using HttpClient client = await _factory.SignIn(owner);
        var id = (await client.GetFromJsonAsync<JsonArray>("/api/exchange-credentials"))!.Single()!["id"]!.GetValue<Guid>();

        HttpResponseMessage response = await client.GetAsync($"/api/exchange-credentials/{id}/authenticator-code");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        problem["code"]!.GetValue<string>().ShouldBe("authenticator_code_unavailable");
        problem["detail"]!.GetValue<string>().ShouldContain("Steam is disabled");
    }

    [Test]
    public async Task ImportNeedsAnActiveUser()
    {
        (int code, string output) = await Import(Input("nobody-with-this-login", "76561190000000009"));

        code.ShouldBe(2);
        output.ShouldBe($"Refused: user_not_found.{Environment.NewLine}");
    }

    private static string Input(string owner, string steamId)
    {
        return $$"""
                 { "owner": "{{owner}}", "label": "Steam main",
                   "authenticator": { "shared_secret": "{{SharedSecret}}", "identity_secret": "aWRlbnRpdHktbm90LXJlYWw=",
                     "device_id": "android:00000000-0000-0000-0000-000000000000",
                     "Session": { "SteamID": {{steamId}}, "RefreshToken": "{{RefreshToken}}" } } }
                 """;
    }

    private async Task<(int Code, string Output)> Import(string input)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IOperatorCommand command = scope.ServiceProvider.GetServices<IOperatorCommand>().Single(candidate => candidate.Name == "steam-import");
        await using var output = new StringWriter();

        int code = await command.Run(new StringReader(input), output, CancellationToken.None);

        return (code, output.ToString());
    }
}