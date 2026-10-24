using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Jobs;

[NonParallelizable]
public sealed class JobsEndpointTests
{
    [Test]
    public async Task QueuedJobIsVisibleToItsOwnerOnly()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient owner = await application.CreateSignedInClient();
        using HttpClient other = await application.CreateSignedInClient();
        Guid id = await JobScenario.Queue(application, await JobScenario.UserId(owner), new TestJobRequest(Steps: 1, StepMilliseconds: 1));

        var job = (await owner.GetFromJsonAsync<JsonNode>($"/api/jobs/{id}"))!;
        using HttpResponseMessage foreign = await other.GetAsync($"/api/jobs/{id}");
        using HttpResponseMessage foreignCancel = await other.PostAsync($"/api/jobs/{id}/cancel", content: null);
        using HttpResponseMessage unknown = await owner.GetAsync($"/api/jobs/{Guid.NewGuid()}");

        job["kind"]!.GetValue<string>().ShouldBe("test_job");
        job["status"]!.GetValue<string>().ShouldBe("Queued");
        job["completed"]!.GetValue<long>().ShouldBe(0);
        job["total"].ShouldBeNull();

        foreach (HttpResponseMessage response in new[] { foreign, foreignCancel, unknown })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadFromJsonAsync<JsonNode>())!["code"]!.GetValue<string>().ShouldBe("job_not_found");
        }
    }

    [Test]
    public async Task QueuedJobIsCancelledAtOnceAndOnlyOnce()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        Guid id = await JobScenario.Queue(application, await JobScenario.UserId(client), new TestJobRequest(Steps: 1, StepMilliseconds: 1));

        using HttpResponseMessage cancelled = await client.PostAsync($"/api/jobs/{id}/cancel", content: null);
        using HttpResponseMessage again = await client.PostAsync($"/api/jobs/{id}/cancel", content: null);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var job = (await cancelled.Content.ReadFromJsonAsync<JsonNode>())!;
        job["status"]!.GetValue<string>().ShouldBe("Cancelled");
        job["finishedAt"].ShouldNotBeNull();
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<JsonNode>())!["code"]!.GetValue<string>().ShouldBe("job_finished");
    }

    [Test]
    public async Task JobsRequireSignIn()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient anonymous = application.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync($"/api/jobs/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}