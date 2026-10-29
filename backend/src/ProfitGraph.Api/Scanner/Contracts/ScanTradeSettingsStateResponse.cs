namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     The owner's trade card settings on the venue; null before they are set.
/// </summary>
public sealed record ScanTradeSettingsStateResponse(ScanTradeSettingsResponse? Settings);