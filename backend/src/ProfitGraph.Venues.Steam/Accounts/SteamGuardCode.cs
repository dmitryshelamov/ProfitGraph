using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     The Steam Guard code of an authenticator (Profit Fox Designer <c>SteamGuardCryptography.Code</c>): HMAC-SHA1 with the
///     shared secret over Steam's 30-second window, five characters of Steam's alphabet. The secret is used in memory only.
/// </summary>
internal static class SteamGuardCode
{
    private const string Alphabet = "23456789BCDFGHJKMNPQRTVWXY";
    private const int MaxSecretBytes = 64;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Whether the shared secret is base64 of 1 to 64 bytes, the only form <see cref="For" /> signs with. The secret is not
    ///     kept or logged.
    /// </summary>
    public static bool Accepts(string sharedSecret)
    {
        Span<byte> secret = stackalloc byte[MaxSecretBytes];

        return Convert.TryFromBase64String(sharedSecret, secret, out int length) && length > 0;
    }

    public static string For(string sharedSecret, DateTimeOffset serverTime)
    {
        byte[] secret = Convert.FromBase64String(sharedSecret);

        if (secret.Length is 0 or > MaxSecretBytes)
        {
            throw new FormatException("The shared secret has an unexpected length.");
        }

        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, serverTime.ToUnixTimeSeconds() / (long)Window.TotalSeconds);
        byte[] hash = HMACSHA1.HashData(secret, counter);
        int offset = hash[^1] & 15;
        uint number = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, length: 4)) & 0x7fffffff;
        var code = new char[5];

        for (var index = 0; index < code.Length; index++)
        {
            code[index] = Alphabet[(int)(number % Alphabet.Length)];
            number /= (uint)Alphabet.Length;
        }

        return new string(code);
    }
}