using System.Text.Json;

using ProfitGraph.Api.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Api.Jobs.Converters;

public static class BackgroundJobResponseConverter
{
    private static readonly JsonSerializerOptions ResultOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static BackgroundJobResponse ToResponse(this BackgroundJobInternal job)
    {
        return new BackgroundJobResponse(
            job.Id,
            job.Kind,
            job.Status.ToString(),
            job.Completed,
            job.Total,
            job.CancelRequested,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            job.Result is null ? null : JsonSerializer.SerializeToElement(job.Result, job.Result.GetType(), ResultOptions),
            job.Failure is null ? null : new BackgroundJobFailureResponse(job.Failure.Code, job.Failure.Message));
    }
}