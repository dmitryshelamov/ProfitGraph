using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Enrollment;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The invented account's sign-in and authenticator linking, in the protocol Profit Fox Designer spoke: an RSA key for the
///     password, protocol buffers for the sign-in, the code Steam would email, and the two-factor service. It records what
///     it was sent so tests can prove what was and was not.
/// </summary>
internal sealed class MockSteamEnrollment : IDisposable
{
    public const string AccountName = "invented-account";
    public const string Password = "invented-password-not-real";
    public const string EmailCode = "MAIL1";
    public const string ActivationCode = "ACT12";
    public const string RecoveryCode = "R98765";
    public const string SharedSecret = "bmV3LXNoYXJlZC1zZWNyZXQtMjA=";
    public const string IdentitySecret = "bmV3LWlkZW50aXR5LXNlY3JldC0y";

    private readonly RSA _key = RSA.Create(keySizeInBits: 2048);
    private bool _codeAccepted;

    public int Confirmation { get; set; } = 2;

    public int AddStatus { get; set; } = 1;

    public bool WantMore { get; set; }

    public int Adds { get; private set; }

    public int Finalizes { get; private set; }

    public DateTimeOffset ServerTime { get; set; }

    public void Dispose()
    {
        _key.Dispose();
    }

    public HttpResponseMessage? Answer(HttpRequestMessage request, string path, IReadOnlyDictionary<string, string> form)
    {
        return path switch
        {
            "/IAuthenticationService/GetPasswordRSAPublicKey/v1/" => Json(
                JsonSerializer.Serialize(
                    new
                    {
                        response = new
                        {
                            publickey_mod = Convert.ToHexString(_key.ExportParameters(includePrivateParameters: false).Modulus!),
                            publickey_exp = Convert.ToHexString(_key.ExportParameters(includePrivateParameters: false).Exponent!),
                            timestamp = "4242"
                        }
                    })),
            "/IAuthenticationService/BeginAuthSessionViaCredentials/v1/" => Begin(form),
            "/IAuthenticationService/UpdateAuthSessionWithSteamGuardCode/v1/" => Code(form),
            "/IAuthenticationService/PollAuthSessionStatus/v1/" => Poll(),
            "/ITwoFactorService/AddAuthenticator/v1/" => Add(form),
            "/ITwoFactorService/FinalizeAddAuthenticator/v1/" => Finalize(form),
            _ when path.StartsWith("/jwt/checkdevice/", StringComparison.Ordinal) => Json("{}"),
            _ => null
        };
    }

    private HttpResponseMessage Begin(IReadOnlyDictionary<string, string> form)
    {
        IReadOnlyList<SteamProtobufField> fields = SteamProtobuf.Read(Convert.FromBase64String(form["input_protobuf_encoded"]));
        string password = Encoding.UTF8.GetString(_key.Decrypt(Convert.FromBase64String(SteamProtobuf.String(fields, number: 3)), RSAEncryptionPadding.Pkcs1));

        if (SteamProtobuf.String(fields, number: 2) != AccountName || password != Password || SteamProtobuf.Number(fields, number: 4) != 4242)
        {
            return Result(eResult: 5, []);
        }

        _codeAccepted = Confirmation == 0;
        byte[] body = SteamProtobuf.Message(
            SteamProtobuf.Integer(number: 1, value: 777),
            SteamProtobuf.Bytes(number: 2, [1, 2, 3]),
            Confirmation == 0 ? [] : SteamProtobuf.Bytes(number: 4, SteamProtobuf.Integer(number: 1, (ulong)Confirmation)),
            SteamProtobuf.Integer(number: 5, ulong.Parse(MockSteam.SteamId, CultureInfo.InvariantCulture)));

        return Result(eResult: 1, body);
    }

    private HttpResponseMessage Code(IReadOnlyDictionary<string, string> form)
    {
        IReadOnlyList<SteamProtobufField> fields = SteamProtobuf.Read(Convert.FromBase64String(form["input_protobuf_encoded"]));
        _codeAccepted = SteamProtobuf.String(fields, number: 3) == EmailCode;

        return Result(_codeAccepted ? 1 : 65, []);
    }

    private HttpResponseMessage Poll()
    {
        return Result(
            eResult: 1,
            _codeAccepted ? SteamProtobuf.Message(SteamProtobuf.Text(number: 3, MockSteam.RefreshToken), SteamProtobuf.Text(number: 4, "access-token-not-real")) : []);
    }

    private HttpResponseMessage Add(IReadOnlyDictionary<string, string> form)
    {
        Adds++;

        return form["access_token"] != "access-token-not-real" || form["steamid"] != MockSteam.SteamId ||
            !form["device_identifier"].StartsWith("android:", StringComparison.Ordinal)
                ? Json("""{"response":{"status":8}}""")
                : Json(
                    JsonSerializer.Serialize(
                        new
                        {
                            response = new
                            {
                                status = AddStatus,
                                shared_secret = SharedSecret,
                                identity_secret = IdentitySecret,
                                revocation_code = RecoveryCode,
                                server_time = ServerTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                                confirm_type = 3
                            }
                        }));
    }

    private HttpResponseMessage Finalize(IReadOnlyDictionary<string, string> form)
    {
        Finalizes++;
        DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(long.Parse(form["authenticator_time"], CultureInfo.InvariantCulture));

        if (form["activation_code"] != ActivationCode)
        {
            return Json("""{"response":{"status":89,"success":false}}""");
        }

        return form["authenticator_code"] == SteamGuardCode.For(SharedSecret, at)
            ? Json(WantMore ? """{"response":{"success":true,"want_more":true}}""" : """{"response":{"success":true,"want_more":false}}""")
            : Json("""{"response":{"status":88,"success":false}}""");
    }

    private static HttpResponseMessage Result(int eResult, byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        response.Headers.TryAddWithoutValidation("x-eresult", eResult.ToString(CultureInfo.InvariantCulture));

        return response;
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}