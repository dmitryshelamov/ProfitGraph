namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Направленное соединение выходного порта одной ноды со входным портом другой.
///     Описывает передачу значения и непосредственную зависимость порядка вычисления.
/// </summary>
public sealed record GraphConnection(PortReference Source, PortReference Target);