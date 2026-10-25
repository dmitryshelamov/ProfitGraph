using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ProfitGraph.Venues.Steam.Confirmations;

/// <summary>
///     The signature of a mobile confirmation request (Profit Fox Designer <c>SteamGuardCryptography.Confirmation</c>):
///     HMAC-SHA1 with the identity secret over Steam's time in seconds, big-endian, followed by the tag. The secret is used in
///     memory only and never logged.
/// </summary>
internal static class SteamConfirmationKey
{
    public const string ListTag = "conf";
    public const string AcceptTag = "accept";

    public static string Sign(string identitySecret, DateTimeOffset serverTime, string tag)
    {
        if (tag is not ListTag and not AcceptTag)
        {
            throw new ArgumentException("Unsupported Steam confirmation tag.", nameof(tag));
        }

        byte[] secret = Convert.FromBase64String(identitySecret);

        if (secret.Length is < 16 or > 64)
        {
            throw new FormatException("The identity secret has an unexpected length.");
        }

        byte[] suffix = Encoding.UTF8.GetBytes(tag);
        var input = new byte[8 + suffix.Length];
        BinaryPrimitives.WriteInt64BigEndian(input, serverTime.ToUnixTimeSeconds());
        suffix.CopyTo(input, index: 8);

        return Convert.ToBase64String(HMACSHA1.HashData(secret, input));
    }
}