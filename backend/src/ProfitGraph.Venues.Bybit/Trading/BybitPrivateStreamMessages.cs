using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Bybit private stream messages: the signed <c>auth</c> request, the Spot or USDT-perpetual order and execution subscription,
///     confirmations, and the orders a message names. The secret's bytes are cleared after signing.
/// </summary>
internal static class BybitPrivateStreamMessages
{
    public const string Subscribe = """{"op":"subscribe","args":["order.spot","execution.spot"]}""";
    public const string SubscribeLinear = """{"op":"subscribe","args":["order.linear","execution.linear"]}""";
    public const string Ping = """{"op":"ping"}""";

    /// <summary>
    ///     HMAC-SHA256 of <c>GET/realtime</c> and the expiry in milliseconds, as lowercase hex.
    /// </summary>
    public static string Auth(string apiKey, string apiSecret, long expiresAt)
    {
        var expires = expiresAt.ToString(CultureInfo.InvariantCulture);
        byte[] secret = Encoding.UTF8.GetBytes(apiSecret);

        try
        {
            string signature = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes("GET/realtime" + expires)));

            return JsonSerializer.Serialize(new { op = "auth", args = new object[] { apiKey, expiresAt, signature } });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>
    ///     Whether the message confirms <paramref name="op" />: true for success, false for a refusal, null for another message.
    /// </summary>
    public static bool? Confirms(string message, string op)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;

            return root.TryGetProperty("op", out JsonElement kind) && kind.GetString() == op && root.TryGetProperty("success", out JsonElement success)
                ? success.GetBoolean()
                : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    ///     The Spot orders an order or execution message names, as client order ID and symbol. Other messages name none;
    ///     a topic message that cannot be read throws.
    /// </summary>
    public static IReadOnlyList<(string ClientOrderId, string Symbol)> Orders(string message)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("topic", out JsonElement topic) ||
            topic.GetString() is not ("order.spot" or "execution.spot" or "order.linear" or "execution.linear"))
        {
            return [];
        }

        try
        {
            return
            [
                .. root.GetProperty("data")
                    .EnumerateArray()
                    .Where(item => !BybitOrderReads.IsFunding(item))
                    .Select(item => (item.GetProperty("orderLinkId").GetString() ?? string.Empty, item.GetProperty("symbol").GetString() ?? string.Empty))
                    .Distinct()
            ];
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid Bybit private stream message.", exception);
        }
    }
}