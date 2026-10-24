namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Mutable indicator samples owned exclusively by one evaluator execution.
/// </summary>
public sealed class CandleIndicatorState
{
    public bool Ready { get; set; }
    internal LibraryIndicatorBuffer? Buffer { get; set; }
    public EngineValue? PreviousA { get; set; }
    public EngineValue? PreviousB { get; set; }
}