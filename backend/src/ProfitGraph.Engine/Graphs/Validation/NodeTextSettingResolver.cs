using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Resolves text settings from explicit literals or the actual captured parameter schema.
/// </summary>
public static class NodeTextSettingResolver
{
    public static string Resolve(GraphNode node, IReadOnlyList<ParamSpec> parameters, string key)
    {
        if (TryResolve(node, parameters, key, out string value))
        {
            return value;
        }

        throw new InvalidOperationException(
            node.Parameters.ContainsKey(key)
                ? $"Expected a text setting for '{key}'."
                : $"Expected a text schema default for '{key}'.");
    }

    public static bool TryResolve(GraphNode node, IReadOnlyList<ParamSpec> parameters, string key, out string value)
    {
        string? resolved = node.Parameters.TryGetValue(key, out ParamValue? parameter)
            ? parameter is FixedParamValue { Value: TextParamLiteral text } ? text.Value : null
            : parameters.Single(spec => spec.Key == key).DefaultValue as string;
        value = resolved ?? string.Empty;

        return resolved is not null;
    }
}