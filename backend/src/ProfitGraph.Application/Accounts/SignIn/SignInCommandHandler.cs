using MediatR;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.SignIn;

internal sealed class SignInCommandHandler(IUserRepository users, IPasswordHasher passwords, TimeProvider clock) : IRequestHandler<SignInCommand, UserContract?>
{
    public async Task<UserContract?> Handle(SignInCommand command, CancellationToken cancellationToken)
    {
        string login = command.Login.Trim();
        User? user = login.Length is > 0 and <= User.MaxLoginLength ? await users.GetByLogin(login, cancellationToken) : null;

        if (user is not { IsActive: true })
        {
            passwords.VerifyDummy(command.Password);

            return null;
        }

        PasswordCheck check = passwords.Verify(user, command.Password);

        if (check == PasswordCheck.Failed)
        {
            return null;
        }

        if (check == PasswordCheck.RehashNeeded)
        {
            user.SetPasswordHash(passwords.Hash(user, command.Password), clock.GetUtcNow());
            await users.Update(user, cancellationToken);
        }

        return UserContract.From(user);
    }
}