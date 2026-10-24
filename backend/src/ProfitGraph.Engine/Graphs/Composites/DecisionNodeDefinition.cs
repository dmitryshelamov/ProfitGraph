using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

/// <summary>
///     Routes an event to exactly one branch through the existing event-filter primitives.
/// </summary>
public sealed class DecisionNodeDefinition : IExpandableNodeDefinition
{
    public StrategyGraph Graph { get; } = CreateGraph();

    public IReadOnlyDictionary<string, IReadOnlyList<PortReference>> InputMappings { get; } =
        new Dictionary<string, IReadOnlyList<PortReference>>
        {
            ["event"] = [new PortReference("yesFilter", "event"), new PortReference("noFilter", "event")],
            ["condition"] = [new PortReference("yesFilter", "condition"), new PortReference("not", "in")]
        };

    public IReadOnlyDictionary<string, PortReference> OutputMappings { get; } =
        new Dictionary<string, PortReference>
        {
            ["yes"] = new PortReference("yesFilter", "out"),
            ["no"] = new PortReference("noFilter", "out")
        };

    public IReadOnlyDictionary<string, IReadOnlyList<PortReference>> ParameterMappings { get; } =
        new Dictionary<string, IReadOnlyList<PortReference>>();

    public string TypeKey => NodeTypeKeys.Decision;
    public NodeCategory Category => NodeCategory.Signal;
    public int SchemaVersion => 1;
    public bool SupportsPhase1 => false;

    public IReadOnlyList<PortSpec> Inputs { get; } =
    [
        new PortSpec("event", PortKind.Event, "Событие", ValueType.Event),
        new PortSpec("condition", PortKind.Condition, "Условие", ValueType.Boolean)
    ];

    public IReadOnlyList<PortSpec> Outputs { get; } =
    [
        new PortSpec("yes", PortKind.Event, "Да", ValueType.Event),
        new PortSpec("no", PortKind.Event, "Нет", ValueType.Event)
    ];

    public IReadOnlyList<ParamSpec> Params { get; } = [];

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(
        NodeContext context,
        IReadOnlyDictionary<string, NodeOutput> inputs,
        ResolvedParams parameters)
    {
        throw new InvalidOperationException("Decision nodes must be expanded before evaluation.");
    }

    private static StrategyGraph CreateGraph()
    {
        var graph = new CompositeGraphBuilder();
        graph.Node("yesFilter", NodeTypeKeys.FilterEvent);
        graph.Node("not", NodeTypeKeys.Not);
        graph.Node("noFilter", NodeTypeKeys.FilterEvent);
        graph.Wire("not", "noFilter", "condition");

        return graph.Build();
    }
}