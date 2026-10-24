using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.SignIn;

internal sealed class SignInCommandHandler(IUserRepository users, IPasswordHasher passwords, TimeProvider clock) : IRequestHandler<SignInCommand, UserInternal?>
{
    public async Task<UserInternal?> Handle(SignInCommand command, CancellationToken cancellationToken)
    {
        string login = command.Login.Trim();
        User? user = login.Length is > 0 and <= User.MaxLoginLength ? await users.GetByLogin(login, cancellationToken) : null;

        if (user is not { IsActive: true })
        {
            passwords.VerifyDummy(command.Password);

            return null;
        }

        switch (passwords.Verify(user, command.Password))
        {
            case PasswordCheck.Failed:
                return null;
            case PasswordCheck.RehashNeeded:
                user.SetPasswordHash(passwords.Hash(user, command.Password), clock.GetUtcNow());
                await users.Update(user, cancellationToken);

                break;
            case PasswordCheck.Valid:
                break;
            default:
                throw new InvalidOperationException("Unknown password check result.");
        }

        return user.ToInternal();
    }
}