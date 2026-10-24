using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     Результат компиляции: план выполнения и диагностики предварительной проверки.
///     При невалидном графе план отсутствует; интерпретатор не должен запускать такой граф.
/// </summary>
public sealed record GraphCompilationResult(CompiledGraph? Graph, GraphValidationResult Validation);