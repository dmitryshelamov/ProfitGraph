namespace ProfitGraph.Engine.Grids;

/// <summary>
///     One result of <see cref="GridFeeCheck" />: an error refuses the run or the bot, a warning is only shown and recorded.
/// </summary>
public sealed record GridFeeFinding(string GridNodeId, bool IsError, string Code, string Message)
{
    public const string ProfitBelowFees = "GridProfitBelowFees";
    public const string StepNearFees = "GridStepNearFees";
}