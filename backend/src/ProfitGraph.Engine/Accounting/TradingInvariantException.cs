using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Нарушение инварианта учёта или недопустимые параметры исполнения: отрицательный баланс, резерв больше остатка,
///     операция над неактивным ордером. Прогон с таким нарушением завершается ошибкой, а не продолжается.
/// </summary>
public sealed class TradingInvariantException(string message) : EngineException(message);