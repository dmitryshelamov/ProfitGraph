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
    ///     What a backtest on this venue does not model, as stable codes a client turns into fixed texts next to the run's
    ///     results (ADR 0023: "the run says so"). Empty for a venue whose replay is exact.
    /// </summary>
    IReadOnlyList<string> BacktestLimits => [];

    /// <summary>
    ///     Whether the owner may confirm a submit absent at the venue when its delivery is unknown: the venue's gateway then
    ///     looks the order up again during reconciliation and rejects it only when the venue shows nothing (Profit Fox
    ///     Designer ADR 0055). False for a venue whose gateway does not check.
    /// </summary>
    bool ConfirmsOrderAbsence => false;

    /// <summary>
    ///     Turns user input into the venue's canonical symbol, or reports that the input is not a symbol of this venue.
    /// </summary>
    bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol);
}