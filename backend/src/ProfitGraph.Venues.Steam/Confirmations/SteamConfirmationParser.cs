using System.Text.Json;

using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Venues.Steam.Confirmations;

/// <summary>
///     Reads <c>/mobileconf/getlist</c>: <c>success</c> and a <c>conf</c> array whose rows carry <c>id</c>, <c>nonce</c>,
///     <c>type</c> and <c>creator_id</c>. A row without any of them, or a repeated ID, makes the whole list unreadable, so
///     nothing is approved from it.
/// </summary>
internal static class SteamConfirmationParser
{
    public static IReadOnlyList<SteamConfirmation>? Read(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || SteamWriteReplyParser.Integer(root, "success") != 1 ||
                !root.TryGetProperty("conf", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var confirmations = new List<SteamConfirmation>();

            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || SteamWriteReplyParser.Text(row, "id") is not { } id ||
                    SteamWriteReplyParser.Text(row, "nonce") is not { } nonce || SteamWriteReplyParser.Text(row, "creator_id") is not { } creator ||
                    SteamWriteReplyParser.Integer(row, "type") is not ({ } type and > 0))
                {
                    return null;
                }

                confirmations.Add(new SteamConfirmation(id, nonce, type, creator));
            }

            return confirmations.DistinctBy(confirmation => confirmation.Id).Count() == confirmations.Count ? confirmations : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}