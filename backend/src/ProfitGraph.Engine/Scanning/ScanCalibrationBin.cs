namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The cards whose predicted chance fell between <paramref name="Lower" /> and <paramref name="Upper" /> in a calibration
///     (ADR 0073): how many there were, their mean predicted chance and the share that reached the target first.
/// </summary>
public sealed record ScanCalibrationBin(decimal Lower, decimal Upper, int Cards, decimal Predicted, decimal Observed);