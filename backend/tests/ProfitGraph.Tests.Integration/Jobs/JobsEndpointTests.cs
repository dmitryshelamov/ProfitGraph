using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Dapper;

using Npgsql;

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
    public async Task ActiveJobsAreTheOwnersQueuedJobsInTheWorkersOrder_CountingOtherUsersJobsAhead()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient owner = await application.CreateSignedInClient();
        using HttpClient other = await application.CreateSignedInClient();
        Guid ownerId = await JobScenario.UserId(owner);
        var request = new TestJobRequest(Steps: 1, StepMilliseconds: 1);
        Guid first = await JobScenario.Queue(application, ownerId, request);
        Guid foreign = await JobScenario.Queue(application, await JobScenario.UserId(other), request);
        Guid second = await JobScenario.Queue(application, ownerId, request);
        Guid cancelled = await JobScenario.Queue(application, ownerId, request);
        (await owner.PostAsync($"/api/jobs/{cancelled}/cancel", content: null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        JsonNode[] jobs = [.. (await owner.GetFromJsonAsync<JsonNode>("/api/jobs/active"))!["jobs"]!.AsArray().Select(job => job!)];
        JsonNode[] foreignJobs = [.. (await other.GetFromJsonAsync<JsonNode>("/api/jobs/active"))!["jobs"]!.AsArray().Select(job => job!)];

        jobs.Select(job => job["id"]!.GetValue<Guid>()).ShouldBe([first, second]);
        jobs.ShouldAllBe(job => job["kind"]!.GetValue<string>() == "test_job" && job["lane"]!.GetValue<string>() == "main"
            && job["status"]!.GetValue<string>() == "Queued" && job["strategyId"] == null);
        jobs[1]["queuePosition"]!.GetValue<int>().ShouldBe(jobs[0]["queuePosition"]!.GetValue<int>() + 2);
        foreignJobs.Select(job => job["id"]!.GetValue<Guid>()).ShouldBe([foreign]);
        foreignJobs[0]["queuePosition"]!.GetValue<int>().ShouldBe(jobs[0]["queuePosition"]!.GetValue<int>() + 1);
    }

    [Test]
    public async Task ActiveJobsTellWhetherTheWorkerBeatsItsHeartbeat()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT INTO worker_heartbeats (name, beat_at) VALUES ('worker', now() - interval '1 hour') ON CONFLICT (name) DO UPDATE SET beat_at = EXCLUDED.beat_at");
        var stale = (await client.GetFromJsonAsync<JsonNode>("/api/jobs/active"))!["workerOnline"]!.GetValue<bool>();
        await connection.ExecuteAsync("UPDATE worker_heartbeats SET beat_at = now() WHERE name = 'worker'");
        var fresh = (await client.GetFromJsonAsync<JsonNode>("/api/jobs/active"))!["workerOnline"]!.GetValue<bool>();

        stale.ShouldBeFalse();
        fresh.ShouldBeTrue();
    }

    [Test]
    public async Task JobsRequireSignIn()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient anonymous = application.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync($"/api/jobs/{Guid.NewGuid()}");
        using HttpResponseMessage active = await anonymous.GetAsync("/api/jobs/active");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        active.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}