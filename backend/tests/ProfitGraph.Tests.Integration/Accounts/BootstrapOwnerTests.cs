using System.Net;
using System.Net.Http.Json;

using Dapper;

using Npgsql;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Accounts;

public sealed class BootstrapOwnerTests
{
    [Test]
    public async Task FirstOwnerComesFromConfigurationOnlyWhenNoAccountExists()
    {
        await using (var migrate = new ProfitGraphApiFactory())
        {
            _ = migrate.Server;
        }

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("DELETE FROM users");
        var settings = new Dictionary<string, string>
        {
            ["Auth:BootstrapOwner:Login"] = "first-owner",
            ["Auth:BootstrapOwner:Password"] = AccountClient.Password
        };

        await using (var first = new ProfitGraphApiFactory(settings))
        {
            using HttpClient client = first.CreateClient();
            (await client.PostAsJsonAsync("/api/auth/login", new { login = "first-owner", password = AccountClient.Password })).StatusCode
                .ShouldBe(HttpStatusCode.OK);
        }

        settings["Auth:BootstrapOwner:Login"] = "second-owner";

        await using (var restarted = new ProfitGraphApiFactory(settings))
        {
            _ = restarted.Server;
        }

        (await connection.QueryAsync<string>("SELECT login FROM users")).ShouldBe(["first-owner"]);
    }

    [Test]
    public void WeakBootstrapPasswordStopsStartup()
    {
        var settings = new Dictionary<string, string> { ["Auth:BootstrapOwner:Login"] = "owner", ["Auth:BootstrapOwner:Password"] = "short" };
        using var factory = new ProfitGraphApiFactory(settings);

        Should.Throw<Exception>(() => _ = factory.Server);
    }
}