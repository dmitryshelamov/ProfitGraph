namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     Switches a preset's Telegram alerts on or off (ADR 0059).
/// </summary>
public sealed record ScanPresetAlertsRequest(bool Enabled);