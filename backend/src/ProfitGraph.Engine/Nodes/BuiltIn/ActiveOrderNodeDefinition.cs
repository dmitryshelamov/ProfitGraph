using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the ActiveOrder node.
/// </summary>
public sealed class ActiveOrderNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.ActiveOrder);
    public bool SupportsPhase1 => false;
    public bool IsAction => false;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.ActiveOrder;
    public NodeClock? EngineClock => NodeClock.Dispatch;
    public NodeCategory Category => NodeCategory.Risk;
    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("tag", PortKind.Series, "tag", ValueType.Tag)
        });

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("exists", PortKind.Condition, "exists", ValueType.Boolean),
            new PortSpec("order", PortKind.Series, "order", ValueType.Order),
            new PortSpec("orderId", PortKind.Series, "orderId", ValueType.OrderId),
            new PortSpec("side", PortKind.Series, "side", ValueType.OrderSide),
            new PortSpec("type", PortKind.Series, "type", ValueType.OrderType),
            new PortSpec("price", PortKind.Series, "price", ValueType.Price),
            new PortSpec("reservedQuote", PortKind.Series, "reservedQuote", ValueType.QuoteAmount),
            new PortSpec("reservedBase", PortKind.Series, "reservedBase", ValueType.BaseAmount)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.Empty<ParamSpec>();

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.AsReadOnly(
        new[]
        {
            new EngineInputSpec("tag", ValueType.Tag, required: true, supportsInline: true)
        });

    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.Empty<ActionEvaluationMode>();

    public bool ProjectsTradingState => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Dispatch;
    }

    public void EvaluateTrading(NodeTradingEvaluationContext context)
    {
        ResolvedEngineParameters parameters = context.Parameters;
        AccountSnapshot account = context.Dispatch.Account;
        EngineValue tag = parameters["tag"];
        OrderSnapshot? order = null;

        if (tag.IsAvailable)
        {
            string selectedTag = tag.Tag;

            for (var index = 0; index < account.Orders.Count; index++)
            {
                OrderSnapshot candidate = account.Orders[index];

                if (candidate.Tag != selectedTag)
                {
                    continue;
                }

                if (order is not null)
                {
                    throw new InvalidOperationException("Sequence contains more than one matching element");
                }

                order = candidate;
            }
        }

        context.Values["exists"] = EngineValue.FromBoolean(order is not null);

        if (order is not null)
        {
            context.Values["order"] = EngineValue.FromOrder(order);
            context.Values["orderId"] = EngineValue.FromOrderId(order.Id);
            context.Values["side"] = EngineValue.FromOrderSide(order.Side);
            context.Values["type"] = EngineValue.FromOrderType(order.Type);
            context.Number(order.Side == OrderSide.Buy ? "reservedQuote" : "reservedBase", order.Reservation);

            if (order.LimitPrice.HasValue)
            {
                context.Number("price", order.LimitPrice.Value);
            }
        }
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        throw new InvalidOperationException("A production trading node requires the causal dispatcher.");
    }
}