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

    public IReadOnlyList<Timeframe> Timeframes { get; } = [.. new[] { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h" }.Select(Timeframe.Parse)];

    public bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol)
    {
        symbol = TInvestSymbol.TryParse(input, out TInvestSymbol? parsed) ? parsed.Symbol : null;

        return symbol is not null;
    }
}