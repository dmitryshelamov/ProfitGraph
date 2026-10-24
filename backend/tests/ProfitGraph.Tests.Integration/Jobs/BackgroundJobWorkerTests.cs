using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Dapper;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Jobs;

/// <summary>
///     The API queues and cancels; a Worker host runs the jobs against the same database, as in production (ADR 0011).
/// </summary>
[NonParallelizable]
public sealed class BackgroundJobWorkerTests
{
    private ProfitGraphApiFactory _application = null!;
    private HttpClient _client = null!;
    private Guid _userId;

    [SetUp]
    public async Task StartApi()
    {
        _application = new ProfitGraphApiFactory();
        _client = await _application.CreateSignedInClient();
        _userId = await JobScenario.UserId(_client);
    }

    [TearDown]
    public async Task StopApi()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Test]
    public async Task WorkerRunsAQueuedJobAndStoresProgressAndResult()
    {
        using IHost worker = WorkerHost.Create();
        await worker.StartAsync();
        Guid id = await JobScenario.Queue(_application, _userId, new TestJobRequest(Steps: 3, StepMilliseconds: 20));

        JsonNode job = await JobScenario.WaitFor(_client, id, JobScenario.Status("Completed"));
        await worker.StopAsync();

        job["completed"]!.GetValue<long>().ShouldBe(3);
        job["total"]!.GetValue<long>().ShouldBe(3);
        job["result"]!["steps"]!.GetValue<int>().ShouldBe(3);
        job["startedAt"].ShouldNotBeNull();
        job["finishedAt"].ShouldNotBeNull();
        job["failure"].ShouldBeNull();
    }

    [Test]
    public async Task WorkerRunsJobsOneAtATimeInQueueOrder()
    {
        using IHost worker = WorkerHost.Create();
        Guid first = await JobScenario.Queue(_application, _userId, new TestJobRequest(Steps: 2, StepMilliseconds: 30));
        Guid second = await JobScenario.Queue(_application, _userId, new TestJobRequest(Steps: 1, StepMilliseconds: 1));
        await worker.StartAsync();

        JsonNode secondJob = await JobScenario.WaitFor(_client, second, JobScenario.Status("Completed"));
        JsonNode firstJob = await JobScenario.WaitFor(_client, first, JobScenario.Status("Completed"));
        await worker.StopAsync();

        firstJob["finishedAt"]!.GetValue<DateTimeOffset>().ShouldBeLessThanOrEqualTo(secondJob["startedAt"]!.GetValue<DateTimeOffset>());
    }

    [Test]
    public async Task ApplicationErrorsKeepTheirCodeAndUnexpectedErrorsAreHidden()
    {
        using IHost worker = WorkerHost.Create();
        await worker.StartAsync();
        Guid known = await JobScenario.Queue(_application, _userId, new TestJobRequest(Steps: 1, StepMilliseconds: 1, "test_failure"));
        Guid unexpected = await JobScenario.Queue(_application, _userId, new TestJobRequest(Steps: 1, StepMilliseconds: 1, ThrowUnexpected: true));

        JsonNode knownJob = await JobScenario.WaitFor(_client, known, JobScenario.Status("Failed"));
        JsonNode unexpectedJob = await JobScenario.WaitFor(_client, unexpected, JobScenario.Status("Failed"));
        await worker.StopAsync();

        knownJob["failure"]!["code"]!.GetValue<string>().ShouldBe("test_failure");
        knownJob["failure"]!["message"]!.GetValue<string>().ShouldBe("The test job failed on purpose.");
        knownJob["completed"]!.GetValue<long>().ShouldBe(1);
        unexpectedJob["failure"]!["code"]!.GetValue<string>().ShouldBe("unexpected_error");
        unexpectedJob["failure"]!["message"]!.GetValue<string>().ShouldNotContain("Unexpected test failure");
    }

    [Test]
    public async Task CancellingARunningJobInTheApiStopsItInTheWorker()
    {
        using IHost worker = WorkerHost.Create();
        await worker.StartAsync();
        Guid id = await JobScenario.Queue(_application, _userId, new TestJobRequest(Steps: 400, StepMilliseconds: 25));
        await JobScenario.WaitFor(_client, id, job => job["status"]!.GetValue<string>() == "Running" && job["completed"]!.GetValue<long>() > 0);

        using HttpResponseMessage requested = await _client.PostAsync($"/api/jobs/{id}/cancel", content: null);
        using HttpResponseMessage repeated = await _client.PostAsync($"/api/jobs/{id}/cancel", content: null);
        JsonNode job = await JobScenario.WaitFor(_client, id, JobScenario.Status("Cancelled"));
        await worker.StopAsync();

        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await requested.Content.ReadFromJsonAsync<JsonNode>())!["cancelRequested"]!.GetValue<bool>().ShouldBeTrue();
        repeated.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        new[] { "job_cancel_requested", "job_finished" }.ShouldContain((await repeated.Content.ReadFromJsonAsync<JsonNode>())!["code"]!.GetValue<string>());
        job["completed"]!.GetValue<long>().ShouldBeLessThan(400);
        job["result"].ShouldBeNull();
    }

    [Test]
    public async Task JobsLeftRunningByAStoppedWorkerFailOnStart()
    {
        Guid id = await JobScenario.Queue(_application, _userId, new TestJobRequest(Steps: 1, StepMilliseconds: 1));
        await Execute("UPDATE background_jobs SET status = 'Running', started_at = now() WHERE id = @id", new { id });
        using IHost worker = WorkerHost.Create();
        await worker.StartAsync();

        JsonNode job = await JobScenario.WaitFor(_client, id, JobScenario.Status("Failed"));
        await worker.StopAsync();

        job["failure"]!["code"]!.GetValue<string>().ShouldBe("background_job_interrupted");
    }

    [Test]
    public async Task ShutdownInterruptsTheRunningJob()
    {
        using IHost worker = WorkerHost.Create();
        await worker.StartAsync();
        Guid id = await JobScenario.Queue(_application, _userId, new TestJobRequest(Steps: 400, StepMilliseconds: 25));
        await JobScenario.WaitFor(_client, id, JobScenario.Status("Running"));

        await worker.StopAsync();
        JsonNode job = await JobScenario.WaitFor(_client, id, JobScenario.Status("Failed"));

        job["failure"]!["code"]!.GetValue<string>().ShouldBe("background_job_interrupted");
    }

    [Test]
    public async Task ReadinessShowsWhetherTheWorkerBeats()
    {
        await Execute("DELETE FROM worker_heartbeats", parameters: null);
        using HttpClient anonymous = _application.CreateClient();
        (await (await anonymous.GetAsync("/api/health/ready")).Content.ReadAsStringAsync()).ShouldBe("Degraded");

        using IHost worker = WorkerHost.Create();
        await worker.StartAsync();
        await WaitForHeartbeat();
        using HttpResponseMessage beating = await anonymous.GetAsync("/api/health/ready");
        await worker.StopAsync();

        beating.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await beating.Content.ReadAsStringAsync()).ShouldBe("Healthy");
        await Execute("UPDATE worker_heartbeats SET beat_at = now() - interval '1 hour'", parameters: null);
        using HttpResponseMessage stale = await anonymous.GetAsync("/api/health/ready");
        stale.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await stale.Content.ReadAsStringAsync()).ShouldBe("Degraded");
    }

    private async Task WaitForHeartbeat()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (await Scalar<long>("SELECT count(*) FROM worker_heartbeats") == 0)
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The Worker did not record a heartbeat.");
            }

            await Task.Delay(millisecondsDelay: 25);
        }
    }

    private async Task Execute(string sql, object? parameters)
    {
        using IServiceScope scope = _application.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NpgsqlConnection>().ExecuteAsync(sql, parameters);
    }

    private async Task<T> Scalar<T>(string sql)
    {
        using IServiceScope scope = _application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<NpgsqlConnection>().ExecuteScalarAsync<T>(sql) ?? throw new InvalidOperationException("No value.");
    }
}