namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     The account a credential trades on as its venue describes it (ADR 0049); every part is optional and none is a secret.
///     <paramref name="Name" /> is the venue's name of the account or the key's note.
/// </summary>
public sealed record AccountIdentityInternal(
    string? AccountId,
    string? Name,
    string? Kind,
    string? AccessLevel,
    bool? ReadOnly,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAt);