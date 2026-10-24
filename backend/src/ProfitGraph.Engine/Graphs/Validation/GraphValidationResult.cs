namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Результат проверки графа с полным списком найденных диагностик.
///     Граф считается валидным только при отсутствии ошибок.
/// </summary>
public sealed record GraphValidationResult(IReadOnlyList<GraphDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
}