using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.StartScanBacktest;

/// <summary>
///     Replays a preset's conditions over the venue's last 30 days of history in a background job (ADR 0059, section 6). The
///     definition may be unsaved; its name only labels the backtest.
/// </summary>
public sealed record StartScanBacktestCommand(Guid UserId, string Venue, ScanPresetDefinitionInternal Definition) : ICommand<ScanBacktestInternal>;