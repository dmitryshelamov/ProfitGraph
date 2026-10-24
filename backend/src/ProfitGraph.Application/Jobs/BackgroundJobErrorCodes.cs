namespace ProfitGraph.Application.Jobs;

public static class BackgroundJobErrorCodes
{
    public const string NotFound = "job_not_found";
    public const string Interrupted = "background_job_interrupted";
    public const string Unexpected = "unexpected_error";
}