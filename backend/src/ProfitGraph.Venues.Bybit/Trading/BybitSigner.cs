using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Signs a Bybit V5 request: HMAC-SHA256 over timestamp, API key, receive window and the exact query (GET) or JSON body
///     (POST), as lowercase hex. The secret's bytes are cleared after use.
/// </summary>
internal static class BybitSigner
{
    internal const string ReceiveWindow = "5000";

    public static void Sign(HttpRequestMessage request, string apiKey, string apiSecret, long timestamp, string payload)
    {
        var timestampText = timestamp.ToString(CultureInfo.InvariantCulture);
        byte[] secret = Encoding.UTF8.GetBytes(apiSecret);
        string signature;

        try
        {
            signature = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(timestampText + apiKey + ReceiveWindow + payload)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        request.Headers.TryAddWithoutValidation("X-BAPI-API-KEY", apiKey);
        request.Headers.TryAddWithoutValidation("X-BAPI-TIMESTAMP", timestampText);
        request.Headers.TryAddWithoutValidation("X-BAPI-RECV-WINDOW", ReceiveWindow);
        request.Headers.TryAddWithoutValidation("X-BAPI-SIGN", signature);
    }
}
