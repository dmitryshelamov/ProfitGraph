using System.Diagnostics.CodeAnalysis;

namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     A login as entered: trimmed, 1 to <see cref="MaxLength" /> characters. Uniqueness ignores case and is enforced by storage.
/// </summary>
public sealed record UserLogin
{
    public const int MaxLength = 100;

    public string Value { get; }

    public UserLogin(string? value)
    {
        if (!TryNormalize(value, out string? normalized))
        {
            throw new AccountRuleException(AccountRuleException.InvalidLogin, $"A login must contain 1 to {MaxLength} characters.");
        }

        Value = normalized;
    }

    /// <summary>
    ///     Reads untrusted input, such as a sign-in attempt, without throwing.
    /// </summary>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out UserLogin? login)
    {
        login = TryNormalize(value, out string? normalized) ? new UserLogin(normalized) : null;

        return login is not null;
    }

    public override string ToString()
    {
        return Value;
    }

    private static bool TryNormalize(string? value, [NotNullWhen(true)] out string? normalized)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        normalized = trimmed.Length is 0 or > MaxLength ? null : trimmed;

        return normalized is not null;
    }
}