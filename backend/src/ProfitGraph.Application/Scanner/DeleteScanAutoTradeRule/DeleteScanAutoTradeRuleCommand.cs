using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Scanner.DeleteScanAutoTradeRule;

/// <summary>
///     Deletes the auto trade rule of the user's own preset with its requests and paper trades.
/// </summary>
public sealed record DeleteScanAutoTradeRuleCommand(Guid UserId, Guid PresetId) : ICommand;