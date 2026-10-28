namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     What a venue tells about the account a credential trades on (ADR 0049): the venue's account identifier, the name or
///     note it keeps, the kind of account, the key's access level, whether it is read-only, its permissions and when it
///     expires. Every part is optional and never a secret; texts have at most <see cref="MaxTextLength" /> characters.
/// </summary>
public sealed record AccountIdentityDetails
{
    public const int MaxTextLength = 128;
    public const int MaxPermissions = 64;
    public const int MaxPermissionLength = 64;

    public string? AccountId { get; }
    public string? Name { get; }
    public string? Kind { get; }
    public string? AccessLevel { get; }
    public bool? ReadOnly { get; }

    /// <summary>
    ///     The key's permissions by the venue's names, in ordinal order without repeats.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public AccountIdentityDetails(
        string? accountId,
        string? name,
        string? kind,
        string? accessLevel,
        bool? readOnly,
        IReadOnlyList<string>? permissions,
        DateTimeOffset? expiresAt)
    {
        string[] distinct = [.. (permissions ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        if (distinct.Length > MaxPermissions ||
            distinct.Any(permission => permission is not { Length: > 0 and <= MaxPermissionLength } || permission.Any(char.IsWhiteSpace)))
        {
            throw new AccountRuleException(
                AccountRuleException.InvalidAccountIdentity,
                $"An account has at most {MaxPermissions} permissions of 1 to {MaxPermissionLength} characters without spaces.");
        }

        AccountId = Text(accountId);
        Name = Text(name);
        Kind = Text(kind);
        AccessLevel = Text(accessLevel);
        ReadOnly = readOnly;
        Permissions = distinct;
        ExpiresAt = expiresAt?.ToUniversalTime();
    }

    public bool Equals(AccountIdentityDetails? other)
    {
        return other is not null && AccountId == other.AccountId && Name == other.Name && Kind == other.Kind && AccessLevel == other.AccessLevel &&
            ReadOnly == other.ReadOnly && Permissions.SequenceEqual(other.Permissions, StringComparer.Ordinal) && ExpiresAt == other.ExpiresAt;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(AccountId, Name, Kind, AccessLevel, ReadOnly, Permissions.Count, ExpiresAt);
    }

    /// <summary>
    ///     A trimmed text, null when empty; a longer one is shortened, since a venue's name or note is shown, not matched.
    /// </summary>
    private static string? Text(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        return trimmed.Length == 0 ? null : trimmed.Length > MaxTextLength ? trimmed[..MaxTextLength] : trimmed;
    }
}