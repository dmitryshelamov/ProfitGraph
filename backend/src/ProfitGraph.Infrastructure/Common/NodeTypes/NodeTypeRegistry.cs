using ProfitGraph.Application.Common.Ports;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Infrastructure.Common.NodeTypes;

/// <summary>
///     An immutable node type catalog ordered by TypeKey; rejects empty and duplicate keys.
/// </summary>
internal sealed class NodeTypeRegistry : INodeTypeRegistry
{
    private readonly IReadOnlyList<INodeDefinition> _catalog;

    public NodeTypeRegistry(IEnumerable<INodeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var byKey = new Dictionary<string, INodeDefinition>(StringComparer.Ordinal);

        foreach (INodeDefinition definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.TypeKey))
            {
                throw new InvalidOperationException($"Node definition '{definition.GetType().Name}' has an empty TypeKey.");
            }

            if (!byKey.TryAdd(definition.TypeKey, definition))
            {
                throw new InvalidOperationException($"Duplicate node TypeKey '{definition.TypeKey}'. Each node type must have a unique key.");
            }
        }

        _catalog = Array.AsReadOnly(byKey.Values.OrderBy(definition => definition.TypeKey, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<INodeDefinition> GetAll()
    {
        return _catalog;
    }
}