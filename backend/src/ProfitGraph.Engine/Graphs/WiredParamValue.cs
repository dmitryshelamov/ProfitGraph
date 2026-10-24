namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Привязка параметра к числовому выходу другой ноды на текущем баре.
///     Создаёт зависимость компиляции, чтобы источник вычислялся раньше потребителя.
/// </summary>
public sealed record WiredParamValue(PortReference Source) : ParamValue;