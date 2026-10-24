namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     План выполнения графа с нодами в детерминированном топологическом порядке.
///     Позволяет интерпретатору обходить готовые зависимости без повторной компиляции на каждом баре.
/// </summary>
public sealed record CompiledGraph(IReadOnlyList<CompiledNode> Nodes);