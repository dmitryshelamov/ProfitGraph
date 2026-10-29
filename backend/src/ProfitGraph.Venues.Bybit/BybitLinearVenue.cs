using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Bybit;

/// <summary>
///     Bybit's USDT perpetuals as the core sees them (ADR 0061): a second venue of the module, with the same kline intervals and
///     symbols as Spot, where a strategy may sell first. One Bybit key trades both markets of the Unified Trading Account, so the
///     venue takes Spot's credentials.
/// </summary>
internal sealed class BybitLinearVenue : IVenueDescriptor
{
    private readonly BybitVenue _spot = new BybitVenue();

    public VenueId Id => BybitMarket.Linear.Venue;

    public string Name => "Bybit Futures";

    public string? Market => BybitMarket.Linear.Title;

    public IReadOnlyList<Timeframe> Timeframes => _spot.Timeframes;

    public bool SupportsShort => true;

    public VenueId? CredentialVenue => BybitMarket.Spot.Venue;

    public IReadOnlyList<string> BacktestLimits { get; } = ["funding_not_modelled"];

    public bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol)
    {
        return _spot.TryParseSymbol(input, out symbol);
    }
}