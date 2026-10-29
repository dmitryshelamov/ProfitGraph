using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.SetScanTradeSettings;

/// <summary>
///     Sets how the owner's trade cards trade on a scanner venue (ADR 0060): the owner's credential of that venue, the quote a stop
///     may lose and the most quote one trade may spend. Trades already created keep their amounts.
/// </summary>
public sealed record SetScanTradeSettingsCommand(Guid OwnerId, string Venue, Guid CredentialId, decimal Risk, decimal MaxAmount)
    : ICommand<ScanTradeSettingsInternal>;