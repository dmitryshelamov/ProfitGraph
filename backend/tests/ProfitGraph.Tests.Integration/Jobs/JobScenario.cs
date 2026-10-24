using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Jobs;

/// <summary>
///     Queues test jobs for a signed-in user and waits for the job to reach a status through the API.
/// </summary>
internal static class JobScenario
{
    internal static async Task<Guid> UserId(HttpClient client)
    {
        return (await client.GetFromJsonAsync<JsonNode>("/api/auth/me"))!["id"]!.GetValue<Guid>();
    }

    internal static async Task<Guid> Queue(ProfitGraphApiFactory application, Guid userId, TestJobRequest request)
    {
        using IServiceScope scope = application.Services.CreateScope();
        BackgroundJobInternal job = await scope.ServiceProvider.GetRequiredService<BackgroundJobQueue>().Enqueue(new UserId(userId), request, CancellationToken.None);

        return job.Id;
    }

    internal static async Task<JsonNode> WaitFor(HttpClient client, Guid id, Func<JsonNode, bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);

        while (true)
        {
            var job = (await client.GetFromJsonAsync<JsonNode>($"/api/jobs/{id}"))!;

            if (condition(job))
            {
                return job;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Job {id} did not reach the expected state: {job.ToJsonString()}");
            }

            await Task.Delay(millisecondsDelay: 25);
        }
    }

    internal static Func<JsonNode, bool> Status(string status)
    {
        return job => job["status"]!.GetValue<string>() == status;
    }
}