using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

internal static class GraphNodeGenerator
{
    public static IReadOnlyList<INodeDefinition> Definitions { get; } =
    [
        new ConstantNodeDefinition(), new DataSourceNodeDefinition(), new SmaNodeDefinition(),
        new ArithmeticNodeDefinition(), new CompareNodeDefinition(), new NotNodeDefinition(),
        new IsAvailableNodeDefinition(), new CrossesAboveNodeDefinition(), new CrossesBelowNodeDefinition(),
        new OnStartNodeDefinition(), new OnCandleClosedNodeDefinition(), new FilterEventNodeDefinition(),
        new MergeEventsNodeDefinition(), new AtrNodeDefinition()
    ];

    public static GraphNode Create(string id, string typeKey = "Constant", IReadOnlyDictionary<string, ParamValue>? parameters = null)
    {
        int schemaVersion = Definitions.SingleOrDefault(definition => definition.TypeKey == typeKey)?.SchemaVersion ?? 1;

        return new GraphNode(id, typeKey, schemaVersion, parameters ?? new Dictionary<string, ParamValue>());
    }

    public static GraphConnection Connect(string source, string target, string targetPort = "in")
    {
        return new GraphConnection(new PortReference(source, "out"), new PortReference(target, targetPort));
    }
}