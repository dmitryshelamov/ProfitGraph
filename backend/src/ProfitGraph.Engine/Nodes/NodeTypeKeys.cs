namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Стабильные ключи встроенных вычислительных нод. Ключи хранятся в снимках стратегий,
///     поэтому меняются только вместе с апгрейдом схемы.
/// </summary>
public static class NodeTypeKeys
{
    public const string DataSource = "DataSource";
    public const string CandleField = "CandleField";
    public const string Constant = "Constant";
    public const string Parameter = "Parameter";
    public const string Sma = "SMA";
    public const string Atr = "ATR";
    public const string Ema = "EMA";
    public const string Rsi = "RSI";
    public const string StandardDeviation = "StdDev";
    public const string Bollinger = "Bollinger";
    public const string Macd = "MACD";
    public const string PreviousCandleValue = "PreviousCandleValue";
    public const string Arithmetic = "Arithmetic";
    public const string Compare = "Compare";
    public const string And = "And";
    public const string Or = "Or";
    public const string Not = "Not";
    public const string CrossesAbove = "CrossesAbove";
    public const string CrossesBelow = "CrossesBelow";
    public const string IsAvailable = "IsAvailable";
    public const string OnStart = "OnStart";
    public const string OnCandleClosed = "OnCandleClosed";
    public const string OnResume = "OnResume";
    public const string MergeEvents = "MergeEvents";
    public const string FilterEvent = "FilterEvent";
    public const string Decision = "Decision";
}