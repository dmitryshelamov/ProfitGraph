using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Accounts;

public sealed class AccountEndpointTests
{
    private ProfitGraphApiFactory _factory = null!;
    private UserContract _owner = null!;
    private HttpClient _ownerClient = null!;

    [OneTimeSetUp]
    public async Task StartApi()
    {
        _factory = new ProfitGraphApiFactory();
        _owner = await AccountClient.CreateAccount(_factory, UserRole.Owner);
        _ownerClient = await AccountClient.SignedIn(_factory, _owner.Login);
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        _ownerClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task ApiRequiresASignedInAccount()
    {
        using HttpClient anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/users", new { login = "x", password = "y", role = "User" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task WrongPasswordIsRejectedWithoutRevealingTheAccount()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { login = _owner.Login, password = "wrong-password-123" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Code(response)).ShouldBe("invalid_credentials");
    }

    [Test]
    public async Task SignedInAccountReadsItselfCaseInsensitively()
    {
        using HttpClient client = await AccountClient.SignedIn(_factory, _owner.Login.ToUpperInvariant());

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        me.GetProperty("id").GetGuid().ShouldBe(_owner.Id);
        me.GetProperty("role").GetString().ShouldBe("Owner");
    }

    [Test]
    public async Task LogoutEndsTheSession()
    {
        using HttpClient client = await AccountClient.SignedIn(_factory, _owner.Login);

        (await client.PostAsync("/api/auth/logout", content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task OwnerCreatesAccountsWithUniqueLoginsAndStrongPasswords()
    {
        var login = $"member-{Guid.NewGuid():N}";

        HttpResponseMessage created = await _ownerClient.PostAsJsonAsync("/api/users", new { login, password = AccountClient.Password, role = "User" });
        HttpResponseMessage duplicate = await _ownerClient.PostAsJsonAsync(
            "/api/users",
            new { login = login.ToUpperInvariant(), password = AccountClient.Password, role = "User" });
        HttpResponseMessage weak = await _ownerClient.PostAsJsonAsync("/api/users", new { login = $"weak-{Guid.NewGuid():N}", password = "short", role = "User" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Code(duplicate)).ShouldBe("login_taken");
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Code(weak)).ShouldBe("validation_failed");
    }

    [Test]
    public async Task OrdinaryUserCannotManageAccounts()
    {
        UserContract user = await AccountClient.CreateAccount(_factory, UserRole.User);
        using HttpClient client = await AccountClient.SignedIn(_factory, user.Login);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users",
            new { login = $"x-{Guid.NewGuid():N}", password = AccountClient.Password, role = "User" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task BlockingEndsTheExistingSessionAndPreventsSignIn()
    {
        UserContract user = await AccountClient.CreateAccount(_factory, UserRole.User);
        using HttpClient client = await AccountClient.SignedIn(_factory, user.Login);

        (await _ownerClient.PostAsync($"/api/users/{user.Id}/block", content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using HttpClient retry = _factory.CreateClient();
        (await retry.PostAsJsonAsync("/api/auth/login", new { login = user.Login, password = AccountClient.Password })).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task OwnerCannotBlockItselfOrAMissingAccount()
    {
        HttpResponseMessage self = await _ownerClient.PostAsync($"/api/users/{_owner.Id}/block", content: null);
        HttpResponseMessage missing = await _ownerClient.PostAsync($"/api/users/{Guid.NewGuid()}/block", content: null);

        self.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Code(self)).ShouldBe("cannot_block_self");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(missing)).ShouldBe("user_not_found");
    }

    [Test]
    public async Task TwoOwnersBlockingEachOtherAtOnceLeaveOneActiveOwner()
    {
        UserContract first = await AccountClient.CreateAccount(_factory, UserRole.Owner);
        UserContract second = await AccountClient.CreateAccount(_factory, UserRole.Owner);
        using HttpClient firstClient = await AccountClient.SignedIn(_factory, first.Login);
        using HttpClient secondClient = await AccountClient.SignedIn(_factory, second.Login);
        await BlockAllOwnersExcept(first.Id, second.Id);

        HttpResponseMessage[] responses = await Task.WhenAll(
            firstClient.PostAsync($"/api/users/{second.Id}/block", content: null),
            secondClient.PostAsync($"/api/users/{first.Id}/block", content: null));

        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        HttpResponseMessage refused = responses.Single(response => response.StatusCode != HttpStatusCode.NoContent);
        refused.StatusCode.ShouldBeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.Unauthorized);
    }

    private static async Task BlockAllOwnersExcept(params Guid[] keep)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("UPDATE users SET status = 'Blocked' WHERE role = 'Owner' AND NOT (id = ANY(@keep))", new { keep });
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }
}