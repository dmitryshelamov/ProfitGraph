namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     The cards of a calibration whose predicted chance fell in one tenth (ADR 0073): its bounds, how many there were, their mean
///     predicted chance and the share that reached the target first. Decimals are exact strings.
/// </summary>
public sealed record ScanCalibrationBinResponse(string Lower, string Upper, int Cards, string Predicted, string Observed);