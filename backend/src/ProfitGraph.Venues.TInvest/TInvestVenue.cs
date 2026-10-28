using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.TInvest;

/// <summary>
///     T-Invest MOEX shares as the core sees them: identifier, name, the timeframes T-Invest candles share with the engine
///     and the symbol rule of <see cref="TInvestSymbol" /> (ADR 0023). Daily candles wait until their grid is confirmed.
/// </summary>
internal sealed class TInvestVenue : IVenueDescriptor
{
    internal static readonly VenueId VenueId = new VenueId("tinvest");

    public VenueId Id => VenueId;

    public string Name => "T-Invest (MOEX)";

    public string? Market => "MOEX";

    public IReadOnlyList<Timeframe> Timeframes { get; } = [.. new[] { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h" }.Select(Timeframe.Parse)];

    /// <summary>
    ///     What a T-Invest backtest approximates (ADR 0023, Consequences): closures and empty intervals come from a reference
    ///     share, weekday holiday sessions replay as ordinary sessions, the price band and the trading status are not
    ///     modelled, a limit crossed by an opening gap fills at its limit, and a candle without trades fills nothing.
    /// </summary>
    public IReadOnlyList<string> BacktestLimits { get; } =
        ["reference_calendar", "holiday_sessions", "price_band", "trading_status", "gap_fills_at_limit", "no_trades_fill_nothing"];

    public bool ConfirmsExpiredOrders => true;

    public bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol)
    {
        symbol = TInvestSymbol.TryParse(input, out TInvestSymbol? parsed) ? parsed.Symbol : null;

        return symbol is not null;
    }
}