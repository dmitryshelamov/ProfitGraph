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
    private const int MinSecretBytes = 16;
    private const int MaxSecretBytes = 64;

    /// <summary>
    ///     Whether the identity secret is base64 of 16 to 64 bytes, the only form <see cref="Sign" /> signs with. The secret is
    ///     not kept or logged.
    /// </summary>
    public static bool Accepts(string identitySecret)
    {
        Span<byte> secret = stackalloc byte[MaxSecretBytes];

        return Convert.TryFromBase64String(identitySecret, secret, out int length) && length >= MinSecretBytes;
    }

    public static string Sign(string identitySecret, DateTimeOffset serverTime, string tag)
    {
        if (tag is not ListTag and not AcceptTag)
        {
            throw new ArgumentException("Unsupported Steam confirmation tag.", nameof(tag));
        }

        byte[] secret = Convert.FromBase64String(identitySecret);

        if (secret.Length is < MinSecretBytes or > MaxSecretBytes)
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