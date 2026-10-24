using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     What a venue module tells the core about itself (ADR 0003, ADR 0010). Data access is a separate port.
/// </summary>
public interface IVenueDescriptor
{
    VenueId Id { get; }

    string Name { get; }

    IReadOnlyList<Timeframe> Timeframes { get; }

    /// <summary>
    ///     Turns user input into the venue's canonical symbol, or reports that the input is not a symbol of this venue.
    /// </summary>
    bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol);
}