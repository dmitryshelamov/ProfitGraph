using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.SetScanAutoTradeMode;

/// <summary>
///     Switches the auto trade rule of the user's own preset off, or to paper with the backtests of its preset's conditions that prove
///     its card (ADR 0071, section 5).
/// </summary>
public sealed record SetScanAutoTradeModeCommand(Guid UserId, Guid PresetId, ScanAutoTradeMode Mode) : ICommand<ScanAutoTradeRuleInternal>;