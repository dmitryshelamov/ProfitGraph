namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Фиксированное числовое или текстовое значение параметра.
///     Используется напрямую при разрешении параметров, без поиска источника или выбора точки Optimize.
/// </summary>
public sealed record FixedParamValue(ParamLiteral Value) : ParamValue;