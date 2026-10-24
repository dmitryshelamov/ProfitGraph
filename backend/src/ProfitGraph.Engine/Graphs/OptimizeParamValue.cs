namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Диапазон выбора числового параметра: минимум, максимум и положительный шаг.
///     Описывает допустимые точки Optimize; конкретное значение передаётся для одного запуска отдельно.
/// </summary>
public sealed record OptimizeParamValue(double Min, double Max, double Step) : ParamValue;