using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Fixtures;

public static class SmaStrategyGraphGenerator
{
    public static StrategyGraph Generate(
        int fastPeriod = 2,
        int slowPeriod = 3,
        decimal buyAmount = 100m,
        bool explicitClock = false,
        bool connectedValues = false)
    {
        StrategyGraph template = RawCompositeSmaStrategyGraphFactory.Create(fastPeriod, slowPeriod, buyAmount);

        if (!explicitClock && !connectedValues)
        {
            return template;
        }

        List<GraphNode> nodes = template.Nodes.Select(node =>
            {
                if (node.Id is not ("buy" or "sell"))
                {
                    return node;
                }

                var parameters = new Dictionary<string, ParamValue>(node.Parameters);

                if (connectedValues && node.Id == "buy")
                {
                    parameters.Remove("amount");
                }

                return new GraphNode(
                    node.Id,
                    node.TypeKey,
                    node.SchemaVersion,
                    parameters,
                    node.DeclaredValueType,
                    explicitClock ? ActionEvaluationMode.OnEvent : node.EvaluationMode);
            })
            .ToList();
        List<GraphConnection> connections = template.Connections.ToList();

        if (connectedValues)
        {
            connections.Add(new GraphConnection(new PortReference("buyAmount", "out"), new PortReference("buy", "amount")));
        }

        if (explicitClock)
        {
            nodes.Add(new GraphNode("clock", "OnCandleClosed", schemaVersion: 1, new Dictionary<string, ParamValue>()));
            connections.Add(new GraphConnection(new PortReference("clock", "out"), new PortReference("buy", "trigger")));
            connections.Add(new GraphConnection(new PortReference("clock", "out"), new PortReference("sell", "trigger")));
        }

        return new StrategyGraph(nodes, connections, template.TemplateKind);
    }
}