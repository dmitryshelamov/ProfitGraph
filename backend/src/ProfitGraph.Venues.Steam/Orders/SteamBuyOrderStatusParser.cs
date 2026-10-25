using System.Text.Json;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Reads <c>/market/getbuyorderstatus/</c> (Profit Fox Designer <c>ReadFinalBuyQuantity</c>): <c>active</c>,
///     <c>quantity</c> and <c>quantity_remaining</c>, as numbers or strings. Counters that do not add up are unreadable.
/// </summary>
internal static class SteamBuyOrderStatusParser
{
    public static SteamBuyOrderStatus? Read(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || SteamWriteReplyParser.Integer(root, "success") != 1 ||
                SteamWriteReplyParser.Integer(root, "quantity") is not { } quantity || SteamWriteReplyParser.Integer(root, "quantity_remaining") is not { } remaining ||
                SteamWriteReplyParser.Integer(root, "active") is not ({ } active and (0 or 1)) || quantity <= 0 || remaining < 0 || remaining > quantity)
            {
                return null;
            }

            return new SteamBuyOrderStatus(active == 1, quantity, remaining);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}