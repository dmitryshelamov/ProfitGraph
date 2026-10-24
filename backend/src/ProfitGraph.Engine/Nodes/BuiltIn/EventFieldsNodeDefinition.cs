using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the EventFields node.
/// </summary>
public sealed class EventFieldsNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.EventFields);
    public bool SupportsPhase1 => false;
    public bool IsAction => false;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.EventFields;
    public NodeClock? EngineClock => NodeClock.Event;
    public NodeCategory Category => NodeCategory.Risk;
    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("event", PortKind.Event, "event", ValueType.Event)
        });

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("out", PortKind.Series, "out")
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.AsReadOnly(
        new[]
        {
            new ParamSpec(
                "field",
                "field",
                ParamValueKind.Text,
                "Fill.Price",
                AllowedValues: Array.AsReadOnly(EventFieldCatalog.Fields.Select(field => field.Key).ToArray()))
        });

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.Empty<EngineInputSpec>();
    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.Empty<ActionEvaluationMode>();

    public IReadOnlyList<EventFieldSpec> EventFields => EventFieldCatalog.Fields;
    public bool EvaluatesDispatch => true;
    public bool EvaluatesUnavailableInputs => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Event;
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        return EventFieldCatalog.Find(NodeTextSettingResolver.Resolve(node, Params, "field"))?.ValueType;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        EngineValue value = context.Inputs["event"];

        return value.IsAvailable ? EventFieldCatalog.Extract(context.Node.EventField!, value.Event) : EngineValue.Unavailable(context.Node.EventField!.ValueType);
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        throw new InvalidOperationException("A production trading node requires the causal dispatcher.");
    }
}