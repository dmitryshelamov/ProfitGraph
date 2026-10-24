namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Описание настраиваемого параметра типа ноды: ключ, подпись, вид значения, значение по умолчанию и числовые ограничения.
///     Определяет доступные настройки для инспектора; границы и шаг также предназначены для будущего Optimize-режима.
/// </summary>
/// <remarks>Описывает схему параметра, а не его разрешённое значение для конкретного вычисления.</remarks>
public sealed record ParamSpec(
    string Key,
    string Label,
    ParamValueKind ValueKind,
    object DefaultValue,
    double? MinValue = null,
    double? MaxValue = null,
    double? Step = null,
    IReadOnlyList<string>? AllowedValues = null);