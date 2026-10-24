namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Причина невалидности графа с кодом, сообщением и ссылкой на ноду, порт или параметр.
///     Позволяет редактору показать ошибку рядом с соответствующим элементом графа.
/// </summary>
public sealed record GraphDiagnostic(GraphDiagnosticCode Code, string Message, string? NodeId = null, string? PortKey = null, string? ParamKey = null);