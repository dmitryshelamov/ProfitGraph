using System.Buffers.Binary;
using System.Security.Cryptography;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The idempotency key T-Invest knows an attempt by (<c>PostOrderRequest.order_id</c>, at most 36 characters): a UUID
///     version 8 from the first 16 bytes of SHA-256 over a fixed domain, the bot, the engine order and the attempt number
///     (ADR 0023). The same attempt always has the same key, so a repeated send cannot place a second order.
/// </summary>
internal static class TInvestOrderRequestIds
{
    private static readonly byte[] Domain = "profitgraph/tinvest-order-request/v1"u8.ToArray();

    public static Guid For(BotId botId, long engineOrderId, int attempt)
    {
        if (engineOrderId <= 0 || attempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "Engine order identifiers and attempt numbers are positive.");
        }

        var input = new byte[Domain.Length + 16 + 8 + 4];
        Domain.CopyTo(input, index: 0);
        botId.Value.ToByteArray(bigEndian: true).CopyTo(input, Domain.Length);
        BinaryPrimitives.WriteInt64BigEndian(input.AsSpan(Domain.Length + 16), engineOrderId);
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(Domain.Length + 24), attempt);
        byte[] hash = SHA256.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash.AsSpan(start: 0, length: 16), bigEndian: true);
    }
}