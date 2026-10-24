using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the Balance node.
/// </summary>
public sealed class BalanceNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.Balance);
    public bool SupportsPhase1 => false;
    public bool IsAction => false;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.Balance;
    public NodeClock? EngineClock => NodeClock.Dispatch;
    public NodeCategory Category => NodeCategory.Risk;
    public int SchemaVersion => 1;
    public IReadOnlyList<PortSpec> Inputs { get; } = Array.Empty<PortSpec>();

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("quoteTotal", PortKind.Series, "quoteTotal", ValueType.QuoteAmount),
            new PortSpec("quoteAvailable", PortKind.Series, "quoteAvailable", ValueType.QuoteAmount),
            new PortSpec("quoteReserved", PortKind.Series, "quoteReserved", ValueType.QuoteAmount),
            new PortSpec("baseTotal", PortKind.Series, "baseTotal", ValueType.BaseAmount),
            new PortSpec("baseAvailable", PortKind.Series, "baseAvailable", ValueType.BaseAmount),
            new PortSpec("baseReserved", PortKind.Series, "baseReserved", ValueType.BaseAmount)
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
        context.Number("quoteTotal", account.QuoteTotal);
        context.Number("quoteAvailable", account.QuoteAvailable);
        context.Number("quoteReserved", account.QuoteReserved);
        context.Number("baseTotal", account.BaseTotal);
        context.Number("baseAvailable", account.BaseAvailable);
        context.Number("baseReserved", account.BaseReserved);
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        throw new InvalidOperationException("A production trading node requires the causal dispatcher.");
    }
}