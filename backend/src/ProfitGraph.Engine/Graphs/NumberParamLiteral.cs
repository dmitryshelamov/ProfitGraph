namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Числовой литерал параметра с явно заданным типом.
///     Позволяет проверять числовые ограничения без object-боксинга и зависимости от JSON.
/// </summary>
public sealed record NumberParamLiteral(double Value) : ParamLiteral;