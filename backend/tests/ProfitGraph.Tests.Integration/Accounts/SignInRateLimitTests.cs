using System.Net;
using System.Net.Http.Json;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Accounts;

public sealed class SignInRateLimitTests
{
    [Test]
    public async Task RepeatedAttemptsForOneLoginAreThrottled()
    {
        await using var factory = new ProfitGraphApiFactory();
        using HttpClient client = factory.CreateClient();
        var login = $"target-{Guid.NewGuid():N}";
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 11; attempt++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { login = login.ToUpperInvariant(), password = "wrong-password-123" });
            statuses.Add(response.StatusCode);
        }

        statuses.Take(10).ShouldAllBe(status => status == HttpStatusCode.Unauthorized);
        statuses[10].ShouldBe(HttpStatusCode.TooManyRequests);
    }
}