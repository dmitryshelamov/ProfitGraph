using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.CreateScanTrade;

/// <summary>
///     Creates a trade from a scanner trade card (ADR 0060) with the prices the card showed: a bot of the owner's level trade strategy on
///     the pair's minutes with the owner's credential, sized by the owner's settings, started at once. It reads the venue's fee rates
///     and starts a bot, so it runs outside a transaction.
/// </summary>
public sealed record CreateScanTradeCommand(
    Guid OwnerId,
    string Venue,
    string Symbol,
    ScanScenario Scenario,
    decimal Entry,
    decimal Stop,
    decimal Target) : ICommand<ScanTradeInternal>, INonTransactionalCommand;