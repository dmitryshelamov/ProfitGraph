namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     The account as its venue last described it (ADR 0049): its identifier, its name or the key's note, its kind, the key's
///     access level, whether it is read-only, its permissions and expiry, and when it was read.
/// </summary>
public sealed record BotAccountIdentityResponse(
    string? AccountId,
    string? Name,
    string? Kind,
    string? AccessLevel,
    bool? ReadOnly,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset ReadAt);