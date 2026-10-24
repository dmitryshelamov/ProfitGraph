using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the HasActiveOrders node.
/// </summary>
public sealed class HasActiveOrdersNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.HasActiveOrders);
    public bool SupportsPhase1 => false;
    public bool IsAction => false;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.HasActiveOrders;
    public NodeClock? EngineClock => NodeClock.Dispatch;
    public NodeCategory Category => NodeCategory.Risk;
    public int SchemaVersion => 1;
    public IReadOnlyList<PortSpec> Inputs { get; } = Array.Empty<PortSpec>();

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("out", PortKind.Condition, "out", ValueType.Boolean)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.AsReadOnly(
        new[]
        {
            new ParamSpec("side", "side", ParamValueKind.Text, "Any", AllowedValues: Array.AsReadOnly(new[] { "Any", "Buy", "Sell" }))
        });

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.Empty<EngineInputSpec>();
    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.Empty<ActionEvaluationMode>();

    public bool ProjectsTradingState => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Dispatch;
    }

    public void EvaluateTrading(NodeTradingEvaluationContext context)
    {
        string side = context.Parameters.TextSettings["side"];
        OrderSide? filter = side == "Any" ? null : ParseSide(side);
        IReadOnlyList<OrderSnapshot> orders = context.Dispatch.Account.Orders;
        var exists = false;

        for (var index = 0; index < orders.Count; index++)
        {
            if (filter is not null && orders[index].Side != filter)
            {
                continue;
            }

            exists = true;

            break;
        }

        context.Values["out"] = EngineValue.FromBoolean(exists);
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        throw new InvalidOperationException("A production trading node requires the causal dispatcher.");
    }

    private static OrderSide ParseSide(string side)
    {
        return side switch
        {
            nameof(OrderSide.Buy) => OrderSide.Buy,
            nameof(OrderSide.Sell) => OrderSide.Sell,
            _ => throw new InvalidOperationException($"Unsupported order side '{side}'.")
        };
    }
}