using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     An invite-only account. Logins are unique case-insensitively; only an active account can sign in.
/// </summary>
public sealed class User : AggregateRoot
{
    public const int MinPasswordLength = 12;

    public UserId Id { get; }
    public UserLogin Login { get; }
    public string PasswordHash { get; private set; }
    public UserRole Role { get; }
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    private User(UserId id, UserLogin login, string passwordHash, UserRole role, UserStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        Login = login;
        PasswordHash = passwordHash;
        Role = role;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    ///     Creates an active account. The password hash is set right after creation because hashing needs the account.
    /// </summary>
    public static User Create(UserId id, UserLogin login, UserRole role, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(login);

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("A user needs a known role.", nameof(role));
        }

        return new User(id, login, string.Empty, role, UserStatus.Active, now, now);
    }

    public static User Restore(
        UserId id,
        UserLogin login,
        string passwordHash,
        UserRole role,
        UserStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(login);

        if (string.IsNullOrWhiteSpace(passwordHash) || !Enum.IsDefined(role) || !Enum.IsDefined(status))
        {
            throw new ArgumentException("Invalid stored user.");
        }

        return new User(id, login, passwordHash, role, status, createdAt, updatedAt);
    }

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Blocks the account. An account cannot block itself, and the last active Owner cannot be blocked.
    ///     Blocking an already blocked account changes nothing.
    /// </summary>
    public void Block(UserId actorId, int activeOwnerCount, DateTimeOffset now)
    {
        if (actorId == Id)
        {
            throw new AccountRuleException(AccountRuleException.CannotBlockSelf, "An account cannot block itself.");
        }

        if (!IsActive)
        {
            return;
        }

        if (Role == UserRole.Owner && activeOwnerCount <= 1)
        {
            throw new AccountRuleException(AccountRuleException.LastOwner, "The last active Owner cannot be blocked.");
        }

        Status = UserStatus.Blocked;
        UpdatedAt = now;
    }
}