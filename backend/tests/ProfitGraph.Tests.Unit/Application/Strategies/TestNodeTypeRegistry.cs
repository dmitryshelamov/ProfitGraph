using ProfitGraph.Application.Common.Ports;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Tests.Unit.Application.Strategies;

internal sealed class TestNodeTypeRegistry(IReadOnlyList<INodeDefinition> definitions) : INodeTypeRegistry
{
    public IReadOnlyList<INodeDefinition> GetAll()
    {
        return definitions;
    }
}