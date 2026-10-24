using ProfitGraph.Engine.Accounting;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

internal sealed class CompositeGraphBuilder
{
    private readonly List<GraphNode> _nodes = [];
    private readonly List<GraphConnection> _connections = [];

    internal static Dictionary<string, ParamValue> Params(params (string Key, ParamValue Value)[] pairs)
    {
        return pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    internal static FixedParamValue Number(decimal value)
    {
        return new FixedParamValue(new DecimalParamLiteral(value));
    }

    internal static FixedParamValue Text(string value)
    {
        return new FixedParamValue(new TextParamLiteral(value));
    }

    internal void Node(string id, string type, Dictionary<string, ParamValue>? parameters = null, ValueType? declared = null, int schemaVersion = 1)
    {
        _nodes.Add(new GraphNode(id, type, schemaVersion, parameters ?? new Dictionary<string, ParamValue>(), declared));
    }

    internal void Reason(string id, RejectionReason reason)
    {
        Node(id, "Constant", Params(("value", new FixedParamValue(new RejectionReasonParamLiteral(reason)))), ValueType.RejectionReason, schemaVersion: 2);
    }

    internal void Compare(string id, string a, string b)
    {
        Node(id, "Compare", Params(("op", Text("="))), schemaVersion: 2);
        Wire(a, id, "a");
        Wire(b, id, "b");
    }

    internal void Binary(string id, string type, string a, string b)
    {
        Node(id, type);
        Wire(a, id, "a");
        Wire(b, id, "b");
    }

    internal void Wire(string source, string target, string input)
    {
        string[] parts = source.Split('.');
        _connections.Add(new GraphConnection(new PortReference(parts[0], parts.Length == 1 ? "out" : parts[1]), new PortReference(target, input)));
    }

    internal StrategyGraph Build()
    {
        return new StrategyGraph(_nodes, _connections);
    }
}