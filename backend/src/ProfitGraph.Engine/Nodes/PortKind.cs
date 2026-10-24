namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Вид связи между нодами: числовой ряд, условие или событие.
///     Позволяет компилятору графа проверять совместимость портов, например запрещать Series → Event.
/// </summary>
public enum PortKind
{
    Series,
    Condition,
    Event
}