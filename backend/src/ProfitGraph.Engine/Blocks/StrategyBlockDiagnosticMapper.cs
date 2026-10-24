using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Blocks;

public static class StrategyBlockDiagnosticMapper
{
    public static StrategyBlockDiagnostic Map(GraphDiagnostic diagnostic, IReadOnlyDictionary<string, StrategySource> sources)
    {
        StrategySource source = diagnostic.NodeId is not null && sources.TryGetValue(diagnostic.NodeId, out StrategySource? found) ? found : new StrategySource();
        string message = diagnostic.Message;

        while (message.StartsWith("Внутри узла (", StringComparison.Ordinal))
        {
            int end = message.IndexOf("): ", StringComparison.Ordinal);

            if (end < 0)
            {
                break;
            }

            message = message[(end + 3)..];
        }

        foreach ((string nodeId, StrategySource mapped) in sources.OrderByDescending(pair => pair.Key.Length))
        {
            message = message.Replace(nodeId, mapped.BlockId ?? mapped.ValueName ?? mapped.ParameterName ?? "strategy", StringComparison.Ordinal);
        }

        return new StrategyBlockDiagnostic(diagnostic.Code, message, source.BlockId, source.ValueName, source.ParameterName, source.CheckIndex);
    }
}