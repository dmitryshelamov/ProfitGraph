using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     Экземпляр ноды, связанный с определением её типа и источниками входных портов.
///     Хранит подготовленные связи; параметры разрешаются отдельно для текущего бара.
/// </summary>
public sealed record CompiledNode(GraphNode Node, INodeDefinition Definition, IReadOnlyList<CompiledInputBinding> Inputs);