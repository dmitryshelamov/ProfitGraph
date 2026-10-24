using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Compatibility adapter for callers selecting a trading node by its trusted behavior identifier.
/// </summary>
public sealed class TradingNodeDefinition : INodeDefinition
{
    private readonly INodeDefinition _definition;

    public string TypeKey => _definition.TypeKey;
    public bool IsAction => _definition.IsAction;
    public TradingNodeBehavior? TradingBehavior => _definition.TradingBehavior;
    public IReadOnlyList<EventFieldSpec> EventFields => _definition.EventFields;
    public NodeClock? EngineClock => _definition.EngineClock;
    public NodeCategory Category => _definition.Category;
    public int SchemaVersion => _definition.SchemaVersion;
    public IReadOnlyList<PortSpec> Inputs => _definition.Inputs;
    public IReadOnlyList<PortSpec> Outputs => _definition.Outputs;
    public IReadOnlyList<ParamSpec> Params => _definition.Params;
    public IReadOnlyList<EngineInputSpec> EngineInputs => _definition.EngineInputs;
    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes => _definition.AllowedEvaluationModes;
    public bool ProjectsTradingState => _definition.ProjectsTradingState;
    public bool EvaluatesDispatch => _definition.EvaluatesDispatch;
    public bool EvaluatesUnavailableInputs => _definition.EvaluatesUnavailableInputs;

    public TradingNodeDefinition(TradingNodeBehavior behavior)
    {
        _definition = behavior switch
        {
            TradingNodeBehavior.Balance => new BalanceNodeDefinition(),
            TradingNodeBehavior.Position => new PositionNodeDefinition(),
            TradingNodeBehavior.ActiveOrder => new ActiveOrderNodeDefinition(),
            TradingNodeBehavior.HasActiveOrders => new HasActiveOrdersNodeDefinition(),
            TradingNodeBehavior.OrderSizePreview => new OrderSizePreviewNodeDefinition(),
            TradingNodeBehavior.OnOrderFilled => new OnOrderFilledNodeDefinition(),
            TradingNodeBehavior.OnOrderCancelled => new OnOrderCancelledNodeDefinition(),
            TradingNodeBehavior.OnOrderRejected => new OnOrderRejectedNodeDefinition(),
            TradingNodeBehavior.EventFields => new EventFieldsNodeDefinition(),
            TradingNodeBehavior.MarketBuy => new MarketBuyNodeDefinition(),
            TradingNodeBehavior.LimitBuy => new LimitBuyNodeDefinition(),
            TradingNodeBehavior.MarketSell => new MarketSellNodeDefinition(),
            TradingNodeBehavior.LimitSell => new LimitSellNodeDefinition(),
            TradingNodeBehavior.CancelOrder => new CancelOrderNodeDefinition(),
            _ => throw new ArgumentOutOfRangeException(nameof(behavior))
        };
    }

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return _definition.ResolveClock(inherited);
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        return _definition.ResolveOutputType(node, input, diagnostics);
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return _definition.EvaluateEngine(context);
    }

    public void EvaluateTrading(NodeTradingEvaluationContext context)
    {
        _definition.EvaluateTrading(context);
    }
}