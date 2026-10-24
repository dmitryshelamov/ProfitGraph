using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Ensures the resume event definition is available to graph engine callers.
/// </summary>
public static class GraphEngineNodeCatalog
{
    public static IReadOnlyList<INodeDefinition> WithResume(IEnumerable<INodeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        INodeDefinition[] catalog = definitions.ToArray();

        return catalog.Any(definition => definition.TypeKey == NodeTypeKeys.OnResume)
            ? catalog
            : [.. catalog, new OnResumeNodeDefinition()];
    }
}