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
    ///     The venue's market the bots trade on, for people (Telegram's filled-order message, ADR 0042), or null when the
    ///     name says it.
    /// </summary>
    string? Market => null;

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
    ///     Whether the owner may confirm that an order the venue had accepted was removed by the venue itself, such as an
    ///     expired day order, with nothing executed: the venue's gateway then checks that the venue no longer knows the order
    ///     before the engine cancels it locally (ADR 0028). False for a venue whose gateway does not check.
    /// </summary>
    bool ConfirmsExpiredOrders => false;

    /// <summary>
    ///     Whether a strategy may sell first on this venue (ADR 0061): a short strategy backtests and runs only where it is true.
    /// </summary>
    bool SupportsShort => false;

    /// <summary>
    ///     Another venue whose credentials this venue also takes (ADR 0061), or null when it takes only its own: bots, scanner trades
    ///     and market data reads on it accept a credential of either venue. A venue of one account, such as futures beside spot,
    ///     names the venue its keys are enrolled for, so one key serves both.
    /// </summary>
    VenueId? CredentialVenue => null;

    /// <summary>
    ///     Turns user input into the venue's canonical symbol, or reports that the input is not a symbol of this venue.
    /// </summary>
    bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol);
}