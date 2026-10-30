namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The backtests of a preset's conditions a rule is judged by (ADR 0071, section 5): the latest one and its kinds of cards, and the
///     latest one over a period in which the venue's reference pair moved the other way with the same kinds; empty without them.
/// </summary>
public sealed record ScanAutoTradeEvidenceInternal(
    ScanBacktestEvidenceInternal? Latest,
    IReadOnlyList<ScanAutoTradeCandidateInternal> LatestCandidates,
    ScanBacktestEvidenceInternal? OtherRegime,
    IReadOnlyList<ScanAutoTradeCandidateInternal> OtherCandidates);