using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Lifecycle emissions to dispatch, or a diagnostic for an ignored stale/invalid update.
/// </summary>
public sealed record ExternalOrderUpdateResult(IReadOnlyList<LifecycleEmission> Emissions, string? DiagnosticCode)
{
    public static ExternalOrderUpdateResult Ignored(string code)
    {
        return new ExternalOrderUpdateResult(Array.Empty<LifecycleEmission>(), code);
    }

    public static ExternalOrderUpdateResult Applied(params LifecycleEmission[] emissions)
    {
        return new ExternalOrderUpdateResult(Array.AsReadOnly(emissions), DiagnosticCode: null);
    }
}