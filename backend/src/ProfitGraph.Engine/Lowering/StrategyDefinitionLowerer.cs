using System.Security.Cryptography;
using System.Text;

using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Lowering;

public sealed class StrategyDefinitionLowerer(IReadOnlyList<INodeDefinition> definitions)
{
    public static string ParameterNodeId(string name)
    {
        return GeneratedId("parameter:" + Encode(name));
    }

    public StrategyLoweringResult Lower(StrategyDefinition definition, IReadOnlyDictionary<string, DecimalOptimizeParamValue>? ranges = null)
    {
        if (StrategyAuthoringLimits.Inspect(definition).Count > 0)
        {
            throw new ArgumentException("Strategy authoring limit exceeded.");
        }

        return new StrategyLoweringRun(definition, ranges, definitions).Run();
    }

    internal static string Encode(string segment)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(segment));
    }

    internal static string GeneratedId(string identity)
    {
        return "$" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}