namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Текстовый литерал параметра: например, имя параметра или выбранная операция.
///     Отделяет текст от чисел для проверки соответствия схеме ноды.
/// </summary>
public sealed record TextParamLiteral(string Value) : ParamLiteral;