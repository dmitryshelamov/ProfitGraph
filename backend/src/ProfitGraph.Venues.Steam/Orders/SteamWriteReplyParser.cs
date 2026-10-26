using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Reads the answer of a market write (Profit Fox Designer <c>SteamMarketClient.Receipt</c>). A 4xx body is read too,
///     because Steam answers a buy that needs a mobile confirmation with a client error carrying <c>need_confirmation</c>,
///     which is read before <c>success</c>. Only a request that never left is <see cref="SteamWriteReceiptKind.NotSent" />,
///     and only a code the owner confirmed rejects; everything else is unknown.
/// </summary>
internal static class SteamWriteReplyParser
{
    public static SteamWriteReceipt Read(SteamRequestResult result, string idField, IReadOnlyCollection<int> definiteRejectionCodes)
    {
        if (result.Outcome is SteamRequestOutcome.NotSent or SteamRequestOutcome.Deferred)
        {
            return new SteamWriteReceipt(SteamWriteReceiptKind.NotSent);
        }

        if (result.Outcome is not (SteamRequestOutcome.Success or SteamRequestOutcome.ClientError) || result.Response is not { Body.Length: > 0 } response)
        {
            return new SteamWriteReceipt(SteamWriteReceiptKind.Unknown);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(response.Body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new SteamWriteReceipt(SteamWriteReceiptKind.Unknown);
            }

            int? code = Integer(root, "success");
            string? steamOrderId = Text(root, idField);
            string? creator = root.TryGetProperty("confirmation", out JsonElement confirmation) && confirmation.ValueKind == JsonValueKind.Object
                ? Text(confirmation, "confirmation_id")
                : null;

            if (Flag(root, "need_confirmation") && (creator ?? steamOrderId) is { Length: > 0 } pending)
            {
                return new SteamWriteReceipt(SteamWriteReceiptKind.NeedsConfirmation, steamOrderId, pending, code);
            }

            if (result.Outcome == SteamRequestOutcome.Success && code == 1)
            {
                return new SteamWriteReceipt(SteamWriteReceiptKind.Accepted, steamOrderId, SteamCode: code);
            }

            return code is { } refused && definiteRejectionCodes.Contains(refused)
                ? new SteamWriteReceipt(SteamWriteReceiptKind.Rejected, SteamCode: refused)
                : new SteamWriteReceipt(SteamWriteReceiptKind.Unknown, SteamCode: code);
        }
        catch (JsonException)
        {
            return new SteamWriteReceipt(SteamWriteReceiptKind.Unknown);
        }
    }

    /// <summary>
    ///     Logs what Steam answered a write, so a refusal can be told apart later: the request outcome, the HTTP status,
    ///     Steam's <c>success</c> and <c>x-eresult</c> codes and the receipt. Never a body, cookie, order ID or amount.
    /// </summary>
    public static SteamWriteReceipt Logged(this SteamWriteReceipt receipt, ILogger? logger, string operation, SteamRequestResult result)
    {
        logger?.Log(
            receipt.Kind == SteamWriteReceiptKind.Accepted ? LogLevel.Information : LogLevel.Warning,
            "Steam {Operation} ended {Receipt}: outcome {Outcome}, HTTP {HttpStatus}, success {SteamCode}, eresult {EResult}.",
            operation,
            receipt.Kind,
            result.Outcome,
            result.Response?.StatusCode,
            receipt.SteamCode,
            result.Response?.EResult);

        return receipt;
    }

    internal static string? Text(JsonElement element, string field)
    {
        if (!element.TryGetProperty(field, out JsonElement value))
        {
            return null;
        }

        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out long number) && number > 0 => number.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

        return text is { Length: > 0 and <= 64 } && text.All(char.IsAsciiDigit) ? text : null;
    }

    internal static int? Integer(JsonElement element, string field)
    {
        if (!element.TryGetProperty(field, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int text) => text,
            JsonValueKind.True => 1,
            _ => null
        };
    }

    private static bool Flag(JsonElement element, string field)
    {
        return element.TryGetProperty(field, out JsonElement value) &&
            (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number == 1));
    }
}