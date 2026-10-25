using System.Text.Json;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     What the owner gives the import on stdin: the ProfitGraph login that will own the credential, a label and the
///     authenticator file of an existing Steam Guard (the common <c>.maFile</c> layout, with the refresh token at its root or
///     in <c>Session</c>). Values are kept only in memory and never printed.
/// </summary>
internal sealed record SteamImportInput(string Owner, string Label, string SteamId, string RefreshToken, string SharedSecret, string IdentitySecret, string DeviceId)
{
    public const int MaxBytes = 64 * 1024;

    /// <summary>
    ///     Reads the input, or names the first field that is missing or malformed without repeating any value.
    /// </summary>
    public static (SteamImportInput? Input, string? Problem) Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || Text(root, "owner") is not { } owner || Text(root, "label") is not { } label)
            {
                return (null, "Give an object with owner, label and authenticator.");
            }

            if (!root.TryGetProperty("authenticator", out JsonElement authenticator) || authenticator.ValueKind != JsonValueKind.Object)
            {
                return (null, "The authenticator file is missing.");
            }

            JsonElement session = authenticator.TryGetProperty("Session", out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : authenticator;
            string? steamId = Text(authenticator, "steamid", "SteamID") ?? Text(session, "SteamID", "steamid");
            string? refreshToken = Text(authenticator, "RefreshToken", "refresh_token") ?? Text(session, "RefreshToken", "refresh_token");

            if (steamId is not { Length: 17 } || !steamId.All(char.IsAsciiDigit))
            {
                return (null, "The authenticator file has no 17-digit Steam ID.");
            }

            if (refreshToken is null)
            {
                return (null, "The authenticator file has no refresh token.");
            }

            if (Text(authenticator, "shared_secret") is not { } shared || Text(authenticator, "identity_secret") is not { } identity ||
                Text(authenticator, "device_id") is not { } device)
            {
                return (null, "The authenticator file needs shared_secret, identity_secret and device_id.");
            }

            return (new SteamImportInput(owner, label, steamId, refreshToken, shared, identity, device), null);
        }
        catch (JsonException)
        {
            return (null, "The input is not valid JSON.");
        }
    }

    public IReadOnlyDictionary<string, string> Fields()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SteamCredentialFields.SteamId] = SteamId,
            [SteamCredentialFields.RefreshToken] = RefreshToken,
            [SteamCredentialFields.SharedSecret] = SharedSecret,
            [SteamCredentialFields.IdentitySecret] = IdentitySecret,
            [SteamCredentialFields.DeviceId] = DeviceId
        };
    }

    public override string ToString()
    {
        return $"SteamImportInput {{ Owner = {Owner}, Redacted }}";
    }

    private static string? Text(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value))
            {
                string? text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                };

                if (text?.Trim() is { Length: > 0 } trimmed)
                {
                    return trimmed;
                }
            }
        }

        return null;
    }
}