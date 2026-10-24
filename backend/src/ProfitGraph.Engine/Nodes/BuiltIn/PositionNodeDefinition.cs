using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the Position node.
/// </summary>
public sealed class PositionNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.Position);
    public bool IsAction => false;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.Position;
    public NodeClock? EngineClock => NodeClock.Dispatch;
    public NodeCategory Category => NodeCategory.Risk;
    public int SchemaVersion => 1;
    public IReadOnlyList<PortSpec> Inputs { get; } = Array.Empty<PortSpec>();

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("quantity", PortKind.Series, "quantity", ValueType.BaseAmount),
            new PortSpec("costBasis", PortKind.Series, "costBasis", ValueType.QuoteAmount),
            new PortSpec("averageCost", PortKind.Series, "averageCost", ValueType.Price),
            new PortSpec("lastBuyPrice", PortKind.Series, "lastBuyPrice", ValueType.Price),
            new PortSpec("realizedPnl", PortKind.Series, "realizedPnl", ValueType.QuoteAmount),
            new PortSpec("barsHeld", PortKind.Series, "Свечей в позиции", ValueType.Integer)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.Empty<ParamSpec>();
    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.Empty<EngineInputSpec>();
    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.Empty<ActionEvaluationMode>();

    public bool ProjectsTradingState => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Dispatch;
    }

    public void EvaluateTrading(NodeTradingEvaluationContext context)
    {
        AccountSnapshot account = context.Dispatch.Account;
        context.Number("quantity", account.BaseTotal);
        context.Number("costBasis", account.CostBasis);
        context.Number("realizedPnl", account.RealizedPnl);

        if (account.BaseTotal > 0m)
        {
            context.Number("averageCost", account.CostBasis / account.BaseTotal);
        }

        if (PositionHoldingPeriod.BarsHeld(context.Dispatch) is { } barsHeld)
        {
            context.Number("barsHeld", barsHeld);
        }

        if (account.LastBuyPrice.HasValue)
        {
            context.Number("lastBuyPrice", account.LastBuyPrice.Value);
        }
    }
}