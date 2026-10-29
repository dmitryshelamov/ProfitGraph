using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.CreateScanTrade;

/// <summary>
///     Creates a trade from a scanner trade card (ADR 0060) with the prices the card showed from the levels of
///     <see cref="Timeframe" />: a bot of the owner's level trade strategy on
///     the pair's minutes with the owner's credential, sized by the owner's settings, started at once. A short card (ADR 0061) runs
///     the owner's short level trade strategy at the settings' leverage. It reads the venue's fee rates
///     and starts a bot, so it runs outside a transaction.
/// </summary>
public sealed record CreateScanTradeCommand(
    Guid OwnerId,
    string Venue,
    string Symbol,
    string Timeframe,
    ScanScenario Scenario,
    decimal Entry,
    decimal Stop,
    decimal Target,
    PositionDirection Direction = PositionDirection.Long) : ICommand<ScanTradeInternal>, INonTransactionalCommand;