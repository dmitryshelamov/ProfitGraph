namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     How well the odds of the owner's other backtests over other periods predicted this backtest's cards (ADR 0073): the source
///     backtests, how many closed cards got a chance, the share that reached the target and the mean predicted chance, the Brier score
///     (lower is better) of the chances, of the random walk's <c>1 / (1 + RR)</c> and of the bands before ADR 0073, and the cards by
///     tenths of the chance. Decimals are exact strings.
/// </summary>
public sealed record ScanCardCalibrationResponse(
    IReadOnlyList<Guid> Sources,
    int Cards,
    string Observed,
    string Predicted,
    string Brier,
    string GeometryBrier,
    string BandsBrier,
    IReadOnlyList<ScanCalibrationBinResponse> Bins);