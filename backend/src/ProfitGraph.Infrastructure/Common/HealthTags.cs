namespace ProfitGraph.Infrastructure.Common;

/// <summary>
///     Health check tags: liveness runs no dependency checks, readiness runs the checks tagged here.
/// </summary>
public static class HealthTags
{
    public const string Ready = "ready";
}