using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Результат проверки вместе с уже обновлённым графом и посчитанными типами, тактами и прогревом.
///     Типы, такты и прогрев заполнены только для строгой проверки без ошибок.
/// </summary>
internal sealed record StrictGraphAnalysis(
    GraphValidationResult Validation,
    StrategyGraph Graph,
    IReadOnlyDictionary<PortReference, ValueType>? OutputTypes,
    IReadOnlyDictionary<string, NodeClock>? Clocks,
    int WarmupCandles);