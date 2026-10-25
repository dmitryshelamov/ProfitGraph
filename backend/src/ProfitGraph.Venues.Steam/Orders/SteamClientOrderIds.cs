using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     The client order ID of a Steam bot's engine order (ADR 0022), in the format of every ProfitGraph venue: <c>pg</c>, the
///     bot GUID's 16 bytes in base64url (22 characters), a hyphen and the positive engine order ID's 8 big-endian bytes in
///     base64url (11 characters). Steam never sees it; the order journal maps it to Steam's own IDs.
/// </summary>
internal static class SteamClientOrderIds
{
    private const string Prefix = "pg";
    private const int Length = 36;

    public static ClientOrderId For(BotId botId, long engineOrderId)
    {
        if (engineOrderId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(engineOrderId), "An engine order identifier is positive.");
        }

        Span<byte> order = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(order, engineOrderId);

        return new ClientOrderId($"{Prefix}{Encode(botId.Value.ToByteArray())}-{Encode(order)}");
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out BotId? botId, out long engineOrderId)
    {
        botId = null;
        engineOrderId = 0;

        if (value is not { Length: Length } || !value.StartsWith(Prefix, StringComparison.Ordinal) || value[24] != '-' ||
            Decode(value[2..24]) is not { Length: 16 } bot || Decode(value[25..]) is not { Length: 8 } order)
        {
            return false;
        }

        long id = BinaryPrimitives.ReadInt64BigEndian(order);

        if (id <= 0 || new Guid(bot) == Guid.Empty)
        {
            return false;
        }

        botId = new BotId(new Guid(bot));
        engineOrderId = id;

        return true;
    }

    private static string Encode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace(oldChar: '+', newChar: '-').Replace(oldChar: '/', newChar: '_');
    }

    private static byte[]? Decode(string text)
    {
        string base64 = text.Replace(oldChar: '-', newChar: '+').Replace(oldChar: '_', newChar: '/');
        base64 += new string(c: '=', (4 - base64.Length % 4) % 4);

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}