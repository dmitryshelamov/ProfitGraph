using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Graphs.Composites;

public sealed record CompositeExpansion(
    StrategyGraph Graph,
    IReadOnlyDictionary<string, string> Owners,
    IReadOnlyDictionary<string, string> InnerIds,
    IReadOnlyList<GraphDiagnostic> Diagnostics)
{
    public GraphDiagnostic MapDiagnostic(GraphDiagnostic diagnostic)
    {
        if (diagnostic.NodeId is null || !Owners.TryGetValue(diagnostic.NodeId, out string? owner))
        {
            return diagnostic;
        }

        string reason = diagnostic.Message;

        foreach ((string generatedId, string innerId) in InnerIds)
        {
            reason = reason.Replace(generatedId, innerId, StringComparison.Ordinal);
        }

        return diagnostic with
        {
            NodeId = owner,
            Message = $"Внутри узла ({InnerIds[diagnostic.NodeId]}): {reason}",
            ParamKey = diagnostic.ParamKey is null
                ? null
                : Graph.Nodes.First(node => node.Id == diagnostic.NodeId).ParameterIdentities.GetValueOrDefault(diagnostic.ParamKey)?.PortKey ??
                diagnostic.ParamKey
        };
    }
}