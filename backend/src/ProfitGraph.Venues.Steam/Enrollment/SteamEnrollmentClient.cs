using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     Steam's sign-in and authenticator requests for linking a new authenticator (Profit Fox Designer
///     <c>SteamAuthenticationClient</c> and <c>SteamEnrollmentClient</c>). The password is encrypted with Steam's RSA key and
///     cleared from memory; nothing a request carries is logged. Requests of one enrollment queue together, and a write is
///     never sent twice: an unclear answer is <c>enrollment_unknown</c>.
/// </summary>
internal sealed class SteamEnrollmentClient(SteamHttpTransport transport, SteamRequestScheduler scheduler)
{
    private const string DeviceName = "ProfitGraph";

    public async Task<SteamSignIn> Begin(Guid enrollmentId, string accountName, string password, CancellationToken cancellationToken)
    {
        string query = Uri.EscapeDataString(Convert.ToBase64String(SteamProtobuf.Text(number: 1, accountName)));
        SteamHttpResponse key = await Send(
            enrollmentId,
            SteamHttpRequest.Get(SteamEndpoint.Api, "/IAuthenticationService/GetPasswordRSAPublicKey/v1/?input_protobuf_encoded=" + query),
            cancellationToken);
        (string modulus, string exponent, ulong timestamp) = RsaKey(key);
        byte[] plain = Encoding.UTF8.GetBytes(password);
        string encrypted;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromHexString(modulus), Exponent = Convert.FromHexString(exponent) });
            encrypted = Convert.ToBase64String(rsa.Encrypt(plain, RSAEncryptionPadding.Pkcs1));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new SteamEnrollmentException("steam_unreadable");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        byte[] request = SteamProtobuf.Message(
            SteamProtobuf.Text(number: 1, DeviceName),
            SteamProtobuf.Text(number: 2, accountName),
            SteamProtobuf.Text(number: 3, encrypted),
            SteamProtobuf.Integer(number: 4, timestamp),
            SteamProtobuf.Integer(number: 5, value: 1),
            SteamProtobuf.Integer(number: 6, value: 3),
            SteamProtobuf.Integer(number: 7, value: 1),
            SteamProtobuf.Text(number: 8, "Mobile"),
            SteamProtobuf.Bytes(
                number: 9,
                SteamProtobuf.Message(SteamProtobuf.Text(number: 1, DeviceName), SteamProtobuf.Integer(number: 2, value: 3), SteamProtobuf.Integer(number: 7, value: 1))),
            SteamProtobuf.Integer(number: 11, value: 8));
        SteamHttpResponse begun = await Protocol(enrollmentId, "BeginAuthSessionViaCredentials", request, cancellationToken);

        if (begun.EResult is 5 or 18)
        {
            throw new SteamEnrollmentException("invalid_login");
        }

        try
        {
            IReadOnlyList<SteamProtobufField> fields = SteamProtobuf.Read(Ok(begun).Body);
            int[] confirmations =
            [
                .. fields.Where(field => field.Number == 4).Select(field => checked((int)SteamProtobuf.Number(SteamProtobuf.Read(field.Data), number: 1)))
            ];
            ulong client = SteamProtobuf.Number(fields, number: 1);
            ulong steamId = SteamProtobuf.Number(fields, number: 5);
            SteamProtobufField? requestId = fields.FirstOrDefault(field => field.Number == 2);

            return client == 0 || steamId == 0 || requestId is null
                ? throw new SteamEnrollmentException("steam_unreadable")
                : new SteamSignIn(
                    client.ToString(CultureInfo.InvariantCulture),
                    Convert.ToBase64String(requestId.Data),
                    steamId.ToString(CultureInfo.InvariantCulture),
                    confirmations);
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException)
        {
            throw new SteamEnrollmentException("steam_unreadable");
        }
    }

    /// <summary>
    ///     Tells Steam the email code will come from this device (Profit Fox Designer called it after an email challenge); its
    ///     answer decides nothing.
    /// </summary>
    public async Task CheckDevice(Guid enrollmentId, SteamSignIn signIn, CancellationToken cancellationToken)
    {
        SteamHttpRequest request = SteamHttpRequest.Write(
            SteamEndpoint.Login,
            "/jwt/checkdevice/" + Uri.EscapeDataString(signIn.SteamId),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["clientid"] = signIn.ClientId, ["steamid"] = signIn.SteamId });
        await scheduler.Execute(enrollmentId, request, token => transport.Send(request, cookies: null, token), cancellationToken);
    }

    /// <summary>
    ///     Sends the email code. False when Steam says the code is wrong, so the owner can type it again.
    /// </summary>
    public async Task<bool> SubmitCode(Guid enrollmentId, SteamSignIn signIn, string code, CancellationToken cancellationToken)
    {
        SteamHttpResponse response = await Protocol(
            enrollmentId,
            "UpdateAuthSessionWithSteamGuardCode",
            SteamProtobuf.Message(
                SteamProtobuf.Integer(number: 1, ulong.Parse(signIn.ClientId, CultureInfo.InvariantCulture)),
                SteamProtobuf.Fixed64(number: 2, ulong.Parse(signIn.SteamId, CultureInfo.InvariantCulture)),
                SteamProtobuf.Text(number: 3, code),
                SteamProtobuf.Integer(number: 4, value: 2)),
            cancellationToken);

        if (response.EResult is 65 or 88)
        {
            return false;
        }

        Ok(response);

        return true;
    }

    /// <summary>
    ///     The sign-in's refresh and access tokens, or null while Steam still confirms it.
    /// </summary>
    public async Task<(string RefreshToken, string AccessToken)?> Poll(Guid enrollmentId, SteamSignIn signIn, CancellationToken cancellationToken)
    {
        SteamHttpResponse response = Ok(
            await Protocol(
                enrollmentId,
                "PollAuthSessionStatus",
                SteamProtobuf.Message(
                    SteamProtobuf.Integer(number: 1, ulong.Parse(signIn.ClientId, CultureInfo.InvariantCulture)),
                    SteamProtobuf.Bytes(number: 2, Convert.FromBase64String(signIn.RequestId))),
                cancellationToken));

        try
        {
            IReadOnlyList<SteamProtobufField> fields = SteamProtobuf.Read(response.Body);
            string refresh = SteamProtobuf.String(fields, number: 3);
            string access = SteamProtobuf.String(fields, number: 4);

            return refresh.Length == 0 || access.Length == 0 ? null : (refresh, access);
        }
        catch (InvalidDataException)
        {
            throw new SteamEnrollmentException("steam_unreadable");
        }
    }

    public async Task<SteamAddedAuthenticator> AddAuthenticator(
        Guid enrollmentId,
        string accessToken,
        string steamId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        JsonElement response = await TwoFactor(
            enrollmentId,
            "AddAuthenticator",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["access_token"] = accessToken,
                ["steamid"] = steamId,
                ["authenticator_type"] = "1",
                ["device_identifier"] = deviceId,
                ["sms_phone_id"] = "1",
                ["version"] = "2"
            },
            cancellationToken);

        try
        {
            int status = response.GetProperty("status").GetInt32();

            if (status != 1)
            {
                throw new SteamEnrollmentException(
                    status switch
                    {
                        2 => "phone_required",
                        29 => "authenticator_exists",
                        _ => "enrollment_refused"
                    });
            }

            int confirmation = response.TryGetProperty("confirm_type", out JsonElement type) && type.TryGetInt32(out int parsed) ? parsed : 0;

            return new SteamAddedAuthenticator(
                response.GetProperty("shared_secret").GetString()!,
                response.GetProperty("identity_secret").GetString()!,
                response.GetProperty("revocation_code").GetString()!,
                confirmation);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or NullReferenceException)
        {
            throw new SteamEnrollmentException("enrollment_unknown");
        }
    }

    /// <summary>
    ///     Activates the authenticator. True when Steam finished, false when it wants another Guard code; a wrong activation
    ///     code is <c>invalid_code</c>, so the owner can type it again.
    /// </summary>
    public async Task<bool> Finalize(
        Guid enrollmentId,
        string accessToken,
        string steamId,
        string guardCode,
        DateTimeOffset serverTime,
        string activationCode,
        CancellationToken cancellationToken)
    {
        JsonElement response = await TwoFactor(
            enrollmentId,
            "FinalizeAddAuthenticator",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["access_token"] = accessToken,
                ["steamid"] = steamId,
                ["authenticator_code"] = guardCode,
                ["authenticator_time"] = serverTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["activation_code"] = activationCode,
                ["validate_sms_code"] = "0"
            },
            cancellationToken);

        if (response.TryGetProperty("status", out JsonElement status) && status.ValueKind == JsonValueKind.Number && status.GetInt32() == 89)
        {
            throw new SteamEnrollmentException("invalid_code");
        }

        if (!response.TryGetProperty("success", out JsonElement success) || success.ValueKind != JsonValueKind.True)
        {
            throw new SteamEnrollmentException("enrollment_unknown");
        }

        return !response.TryGetProperty("want_more", out JsonElement more) || more.ValueKind != JsonValueKind.True;
    }

    public async Task<DateTimeOffset> ServerTime(Guid enrollmentId, CancellationToken cancellationToken)
    {
        SteamHttpResponse response = await Send(
            enrollmentId,
            new SteamHttpRequest(SteamEndpoint.Api, "/ITwoFactorService/QueryTime/v1/", new Dictionary<string, string>(), IsRead: true),
            cancellationToken);

        try
        {
            using JsonDocument document = JsonDocument.Parse(response.Body);

            return DateTimeOffset.FromUnixTimeSeconds(
                long.Parse(document.RootElement.GetProperty("response").GetProperty("server_time").ToString(), CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new SteamEnrollmentException("steam_unreadable");
        }
    }

    private static (string Modulus, string Exponent, ulong Timestamp) RsaKey(SteamHttpResponse response)
    {
        try
        {
            if (response.MediaType == "application/x-protobuf")
            {
                IReadOnlyList<SteamProtobufField> fields = SteamProtobuf.Read(response.Body);

                return (SteamProtobuf.String(fields, number: 1), SteamProtobuf.String(fields, number: 2), SteamProtobuf.Number(fields, number: 3));
            }

            using JsonDocument document = JsonDocument.Parse(response.Body);
            JsonElement key = document.RootElement.GetProperty("response");

            return (key.GetProperty("publickey_mod").GetString()!, key.GetProperty("publickey_exp").GetString()!,
                ulong.Parse(key.GetProperty("timestamp").ToString(), CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new SteamEnrollmentException("steam_unreadable");
        }
    }

    private async Task<JsonElement> TwoFactor(Guid enrollmentId, string method, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        SteamHttpResponse response;

        try
        {
            response = await Send(
                enrollmentId,
                SteamHttpRequest.Write(SteamEndpoint.Api, "/ITwoFactorService/" + method + "/v1/", form, multipart: true),
                cancellationToken);
        }
        catch (SteamEnrollmentException)
        {
            // A write without a clear answer may have taken effect; it is never sent again.
            throw new SteamEnrollmentException("enrollment_unknown");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(response.Body);

            return document.RootElement.GetProperty("response").Clone();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new SteamEnrollmentException("enrollment_unknown");
        }
    }

    private Task<SteamHttpResponse> Protocol(Guid enrollmentId, string method, byte[] request, CancellationToken cancellationToken)
    {
        return Send(
            enrollmentId,
            new SteamHttpRequest(
                SteamEndpoint.Api,
                "/IAuthenticationService/" + method + "/v1/",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["input_protobuf_encoded"] = Convert.ToBase64String(request) },
                IsRead: false,
                Class: SteamRequestClass.Status),
            cancellationToken,
            acceptResult: true);
    }

    /// <summary>
    ///     Sends one request in the enrollment's queue. Steam's result code is left to the caller when
    ///     <paramref name="acceptResult" /> asks for it; any other failure is <c>steam_unavailable</c>.
    /// </summary>
    private async Task<SteamHttpResponse> Send(Guid enrollmentId, SteamHttpRequest request, CancellationToken cancellationToken, bool acceptResult = false)
    {
        SteamRequestResult result = await scheduler.Execute(enrollmentId, request, token => transport.Send(request, cookies: null, token), cancellationToken);

        return result.Outcome == SteamRequestOutcome.Success && result.Response is { } response && (acceptResult || response.EResult is null or 1)
            ? response
            : throw new SteamEnrollmentException("steam_unavailable");
    }

    private static SteamHttpResponse Ok(SteamHttpResponse response)
    {
        return response.EResult is null or 1 ? response : throw new SteamEnrollmentException("steam_refused");
    }
}