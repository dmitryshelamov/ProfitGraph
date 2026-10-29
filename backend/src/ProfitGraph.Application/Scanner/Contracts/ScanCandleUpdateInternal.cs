using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A 1-minute candle of one instrument from a scan stream: closed, or still forming when <see cref="Closed" /> is false.
/// </summary>
public sealed record ScanCandleUpdateInternal(string Symbol, ScanCandle Candle, bool Closed);