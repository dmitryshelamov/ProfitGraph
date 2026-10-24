using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Bybit;

/// <summary>
///     Bybit Spot as the core sees it: identifier, name, kline intervals and the symbol rule.
/// </summary>
internal sealed class BybitVenue : IVenueDescriptor
{
    private const int MaxSymbolLength = 32;

    internal static readonly VenueId VenueId = new VenueId("bybit");

    public VenueId Id => VenueId;

    public string Name => "Bybit";

    /// <summary>
    ///     Spot kline intervals that the engine's timeframe grid also supports.
    /// </summary>
    public IReadOnlyList<Timeframe> Timeframes { get; } =
        [.. new[] { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "12h", "1d" }.Select(Timeframe.Parse)];

    /// <summary>
    ///     Accepts a pair as people type it ("btc/usdt", " BTCUSDT ") and returns Bybit's form ("BTCUSDT").
    /// </summary>
    public bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol)
    {
        string canonical = input?.Trim().Replace("/", string.Empty, StringComparison.Ordinal).ToUpperInvariant() ?? string.Empty;
        symbol = canonical.Length is > 0 and <= MaxSymbolLength && canonical.All(char.IsAsciiLetterOrDigit) ? new InstrumentSymbol(canonical) : null;

        return symbol is not null;
    }
}