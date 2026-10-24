using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     Подготовленная привязка входного порта к выходу ноды-источника.
///     Используется интерпретатором для сборки входов из результатов текущего бара.
/// </summary>
public sealed record CompiledInputBinding(string InputKey, PortReference Source)
{
    internal int SourceSlot { get; init; } = -1;
}