using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The client order identifier of a bot's engine order (ADR 0023): the venue-neutral <c>pg</c> format of every live
///     venue, <c>pg</c>, the bot GUID's 16 bytes in base64url, a hyphen and the positive engine order ID's 8 big-endian
///     bytes in base64url. T-Invest never sees it; the attempts table maps it to the request IDs T-Invest knows.
/// </summary>
internal static class TInvestClientOrderIds
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

    /// <summary>
    ///     The part of every client order identifier of a bot that names the bot.
    /// </summary>
    public static string PrefixOf(BotId botId)
    {
        return Prefix + Encode(botId.Value.ToByteArray());
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