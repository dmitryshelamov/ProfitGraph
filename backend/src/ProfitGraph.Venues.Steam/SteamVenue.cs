using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Steam;

/// <summary>
///     Steam Community Market as the core sees it: identifier, name, the timeframes of its median history and the symbol
///     rule of <see cref="SteamInstrument" /> (ADR 0022).
/// </summary>
internal sealed class SteamVenue : IVenueDescriptor
{
    internal static readonly VenueId VenueId = new VenueId("steam");

    public VenueId Id => VenueId;

    public string Name => "Steam Community Market";

    /// <summary>
    ///     Steam reports hourly medians for about the last 30 days and daily medians before that.
    /// </summary>
    public IReadOnlyList<Timeframe> Timeframes { get; } = [Timeframe.Parse("1h"), Timeframe.Parse("1d")];

    public bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol)
    {
        symbol = SteamInstrument.TryParse(input, out SteamInstrument? instrument) ? instrument.Symbol : null;

        return symbol is not null;
    }
}