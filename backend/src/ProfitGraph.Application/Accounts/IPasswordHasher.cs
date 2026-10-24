using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts;

public interface IPasswordHasher
{
    string Hash(User user, string password);

    PasswordCheck Verify(User user, string password);

    /// <summary>
    ///     Verifies against a fixed dummy hash, so a missing or blocked account takes as long as a wrong password.
    /// </summary>
    void VerifyDummy(string password);
}