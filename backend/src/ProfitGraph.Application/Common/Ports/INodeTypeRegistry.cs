using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Application.Common.Ports;

/// <summary>
///     The node type catalog, independent of how Infrastructure assembles it.
/// </summary>
public interface INodeTypeRegistry
{
    IReadOnlyList<INodeDefinition> GetAll();
}