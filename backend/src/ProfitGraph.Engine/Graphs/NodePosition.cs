namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Координаты ноды на холсте редактора.
///     Нужны для восстановления сохранённой раскладки и не влияют на выполнение графа.
/// </summary>
public sealed record NodePosition(double X, double Y);