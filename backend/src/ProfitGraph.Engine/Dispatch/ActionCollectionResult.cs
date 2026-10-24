namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Materialized requests and suppression diagnostics for one occurrence.
/// </summary>
public sealed class ActionCollectionResult
{
    public IReadOnlyList<ActionRequest> Requests { get; }
    public IReadOnlyList<ActionSkipDiagnostic> Skips { get; }

    public ActionCollectionResult(IEnumerable<ActionRequest> requests, IEnumerable<ActionSkipDiagnostic> skips)
    {
        Requests = Array.AsReadOnly(requests.ToArray());
        Skips = Array.AsReadOnly(skips.ToArray());
    }
}