using System.Net;
using System.Text.Json;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Accounts;

namespace ProfitGraph.Tests.Integration.Common;

public sealed class HealthEndpointTests
{
    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task LivenessReportsHealthyWithoutDependencies()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Test]
    public async Task ReadinessChecksTheDatabase()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Test]
    public async Task UnknownRouteRequiresSignInAndThenReturnsProblemDetails()
    {
        using HttpClient anonymous = _factory.CreateClient();
        UserInternal user = await AccountClient.CreateAccount(_factory, UserRole.User);
        using HttpClient client = await AccountClient.SignedIn(_factory, user.Login);

        (await anonymous.GetAsync("/api/missing")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        HttpResponseMessage response = await client.GetAsync("/api/missing");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("http_404");
    }
}