using Microsoft.AspNetCore.Identity;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Infrastructure.Accounts;

/// <summary>
///     ASP.NET Core Identity password hashing (PBKDF2 with a per-password salt and versioned format).
/// </summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly User DummyUser = User.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"), "dummy", UserRole.User, DateTimeOffset.UnixEpoch);

    private readonly PasswordHasher<User> _hasher = new PasswordHasher<User>();
    private readonly Lazy<string> _dummyHash;

    public IdentityPasswordHasher()
    {
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(DummyUser, Guid.NewGuid().ToString("N")));
    }

    public string Hash(User user, string password)
    {
        return _hasher.HashPassword(user, password);
    }

    public PasswordCheck Verify(User user, string password)
    {
        return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Valid,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.RehashNeeded,
            _ => PasswordCheck.Failed
        };
    }

    public void VerifyDummy(string password)
    {
        _hasher.VerifyHashedPassword(DummyUser, _dummyHash.Value, password);
    }
}