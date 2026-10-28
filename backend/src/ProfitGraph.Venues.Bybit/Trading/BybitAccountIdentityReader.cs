using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Describes the key's account with a signed, read-only <c>GET /v5/user/query-api</c> (ADR 0049): the account's UID, the
///     key's note, whether the account is the main one or a sub-account, read-only or read-write access, every permission as
///     <c>Group:Name</c> and the expiry. The key itself, its secret and IP list in the answer are never read. The UID is
///     required; failures carry only a reason, an HTTP status or a Bybit code.
/// </summary>
internal sealed class BybitAccountIdentityReader(BybitReadClient read) : IVenueAccountIdentityReader
{
    public VenueId Venue => BybitVenue.VenueId;

    public async Task<AccountIdentityReadInternal> Read(ExchangeCredentialSecretsInternal credential, CancellationToken cancellationToken)
    {
        BybitRestResult result = await read.Get(credential, "/v5/user/query-api", string.Empty, cancellationToken);

        if (result.Outcome != BybitRestOutcome.Accepted)
        {
            // A key Bybit does not know, refuses by IP or lacks permission for is refused, not a venue outage.
            return AccountIdentityReadInternal.Failed(
                result is { Outcome: BybitRestOutcome.Rejected } or { Code: 10003 or 10005 or 10010 } ? "venue_rejected" : "venue_unavailable",
                result.Code is { } code ? code.ToString(CultureInfo.InvariantCulture) : null);
        }

        try
        {
            JsonElement key = result.Body.GetProperty("result");

            if (Text(key, "userID") is not { } userId)
            {
                return AccountIdentityReadInternal.Failed("unsupported_response", "userID");
            }

            return AccountIdentityReadInternal.Observed(
                new AccountIdentityInternal(
                    userId,
                    Text(key, "note"),
                    key.TryGetProperty("isMaster", out JsonElement master) && master.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? master.GetBoolean() ? "main" : "sub"
                        : null,
                    ReadOnly(key) switch
                    {
                        true => "read_only",
                        false => "read_write",
                        null => null
                    },
                    ReadOnly(key),
                    Permissions(key),
                    ExpiresAt(key)));
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return AccountIdentityReadInternal.Failed("unsupported_response", "result");
        }
    }

    /// <summary>
    ///     A string or a number as text; empty or missing is unknown.
    /// </summary>
    private static string? Text(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool? ReadOnly(JsonElement key)
    {
        return key.TryGetProperty("readOnly", out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() != 0 : null;
    }

    private static string[] Permissions(JsonElement key)
    {
        if (!key.TryGetProperty("permissions", out JsonElement groups) || groups.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return
        [
            .. groups.EnumerateObject()
                .Where(group => group.Value.ValueKind == JsonValueKind.Array)
                .SelectMany(group => group.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    .Select(item => $"{group.Name}:{item.GetString()}"))
        ];
    }

    /// <summary>
    ///     The expiry Bybit reports as an ISO 8601 instant; a key without one never expires.
    /// </summary>
    private static DateTimeOffset? ExpiresAt(JsonElement key)
    {
        return Text(key, "expiredAt") is { } text &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at)
                ? at
                : null;
    }
}