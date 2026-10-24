namespace ProfitGraph.Engine.Blocks;

public sealed record StrategyBlockValidationResult(IReadOnlyList<StrategyBlockDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
}