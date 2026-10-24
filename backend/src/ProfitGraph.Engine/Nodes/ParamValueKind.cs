namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Вид значения параметра ноды: число или текст.
///     Позволяет инспектору выбрать подходящий редактор, например числовой контрол для period или поле для symbol.
/// </summary>
public enum ParamValueKind
{
    Number,
    Text
}