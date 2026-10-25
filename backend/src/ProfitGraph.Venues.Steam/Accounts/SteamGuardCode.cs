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
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    public static string For(string sharedSecret, DateTimeOffset serverTime)
    {
        byte[] secret = Convert.FromBase64String(sharedSecret);

        if (secret.Length is 0 or > 64)
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