using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.StartScanBacktest;

/// <summary>
///     Replays a preset's conditions over 30 days of the venue's history in a background job (ADR 0059, section 6): the last 30 days, or
///     30 days ending <paramref name="DaysBack" /> days ago, so that a rule can be checked on another market (ADR 0071, section 5). The
///     definition may be unsaved; its name only labels the backtest.
/// </summary>
public sealed record StartScanBacktestCommand(Guid UserId, string Venue, ScanPresetDefinitionInternal Definition, int DaysBack = 0)
    : ICommand<ScanBacktestInternal>;