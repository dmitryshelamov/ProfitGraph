using System.Security.Cryptography;
using System.Text;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     A one-time code that links the Telegram chat it is sent from to a user (ADR 0020). Only its hash is stored; it
///     expires after ten minutes, and a new code replaces the user's earlier one.
/// </summary>
public sealed record TelegramLinkCode(string CodeHash, UserId UserId, DateTimeOffset ExpiresAt)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     A new random code, returned in plain text once, and its stored form.
    /// </summary>
    public static IssuedTelegramLinkCode Issue(UserId userId, DateTimeOffset now)
    {
        string code = RandomNumberGenerator.GetString(Alphabet, length: 10);

        return new IssuedTelegramLinkCode(code, new TelegramLinkCode(Hash(code), userId, now + Lifetime));
    }

    public static string Hash(string code)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));
    }

    public bool IsExpired(DateTimeOffset now)
    {
        return ExpiresAt <= now;
    }
}