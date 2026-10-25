namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     What a Steam market write ended with. <see cref="SteamCode" /> is Steam's own <c>success</c> number when it gave one.
/// </summary>
internal sealed record SteamWriteReceipt(
    SteamWriteReceiptKind Kind,
    string? SteamOrderId = null,
    string? ConfirmationCreator = null,
    int? SteamCode = null);