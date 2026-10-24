using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Fixtures;

public static class LadderStrategyGraphGenerator
{
    public static StrategyGraph Generate(
        decimal buyAmount = 100m,
        decimal allocationCap = 300m,
        decimal drop = 0.1m,
        decimal rise = 0.1m,
        bool connectedValues = false)
    {
        StrategyGraph template = RawCompositeLadderStrategyGraphFactory.Create(buyAmount, allocationCap, drop, rise);

        if (!connectedValues)
        {
            return template;
        }

        var amountConnections = new Dictionary<string, GraphConnection>();
        GraphNode[] nodes = template.Nodes.Select(node =>
            {
                if (node.Parameters.GetValueOrDefault("amount") is not WiredParamValue amount)
                {
                    return node;
                }

                amountConnections.Add(node.Id, new GraphConnection(amount.Source, new PortReference(node.Id, "amount")));
                var parameters = new Dictionary<string, ParamValue>(node.Parameters);
                parameters.Remove("amount");

                return new GraphNode(node.Id, node.TypeKey, node.SchemaVersion, parameters, node.DeclaredValueType, node.EvaluationMode);
            })
            .ToArray();
        var connections = new List<GraphConnection>();

        foreach (GraphConnection connection in template.Connections)
        {
            connections.Add(connection);

            if (connection.Target.PortKey == "enabled" && amountConnections.TryGetValue(connection.Target.NodeId, out GraphConnection? amount))
            {
                connections.Add(amount);
            }
        }

        return new StrategyGraph(nodes, connections, template.TemplateKind);
    }
}