using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Контекст одного вычисления ноды: текущий бар, инструмент, таймфрейм и доступ к истории свечей.
///     Передаёт внешние данные для расчёта бар за баром, не храня состояние поведения ноды.
/// </summary>
/// <remarks>LookBack возвращает окно, заканчивающееся текущим баром, и отклоняет запрос при недостаточной истории.</remarks>
public sealed class NodeContext
{
    private readonly IReadOnlyList<Candle> _history;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<NodeOutput>> _inputHistory;

    public int BarIndex { get; }

    public string Symbol { get; }

    public string Timeframe { get; }

    public Candle CurrentCandle => _history[BarIndex];

    public NodeContext(
        int barIndex,
        IReadOnlyList<Candle> history,
        string symbol,
        string timeframe,
        IReadOnlyDictionary<string, IReadOnlyList<NodeOutput>>? inputHistory = null)
    {
        _history = history;

        if (barIndex < 0 || barIndex >= history.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(barIndex));
        }

        BarIndex = barIndex;
        Symbol = symbol;
        Timeframe = timeframe;
        _inputHistory = inputHistory ?? new Dictionary<string, IReadOnlyList<NodeOutput>>();
    }

    public IReadOnlyList<NodeOutput> InputHistory(string inputKey)
    {
        return _inputHistory.TryGetValue(inputKey, out IReadOnlyList<NodeOutput>? values) ? values : Array.Empty<NodeOutput>();
    }

    public IReadOnlyList<Candle> LookBack(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        if (count > BarIndex + 1)
        {
            throw new InvalidOperationException($"Requested {count} bars of history at bar {BarIndex}, only {BarIndex + 1} available.");
        }

        return _history.Skip(BarIndex + 1 - count).Take(count).ToList();
    }
}