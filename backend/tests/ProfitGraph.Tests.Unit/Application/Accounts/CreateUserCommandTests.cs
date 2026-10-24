using NSubstitute;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.CreateUser;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Unit.Common;

namespace ProfitGraph.Tests.Unit.Application.Accounts;

public sealed class CreateUserCommandTests
{
    [TestCase("", "password-123456", "invalid_login")]
    [TestCase("alice", "short", "weak_password")]
    public void ValidatorRejectsInvalidInput(string login, string password, string code)
    {
        new CreateUserCommandValidator().Validate(new CreateUserCommand(login, password, UserRole.User)).Errors.ShouldContain(error => error.ErrorCode == code);
    }

    [Test]
    public void ValidatorRejectsAnUnknownRole()
    {
        new CreateUserCommandValidator().Validate(new CreateUserCommand("alice", "password-123456", (UserRole)42)).IsValid.ShouldBeFalse();
    }

    [Test]
    public async Task TakenLoginIsAConflict()
    {
        var users = Substitute.For<IUserRepository>();
        users.TryAdd(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(false);
        var passwords = Substitute.For<IPasswordHasher>();
        passwords.Hash(Arg.Any<User>(), Arg.Any<string>()).Returns("hash");
        var handler = new CreateUserCommandHandler(users, passwords, new FixedClock(DateTimeOffset.UnixEpoch));

        var conflict = await Should.ThrowAsync<StateConflictException>(() =>
            handler.Handle(new CreateUserCommand("alice", "password-123456", UserRole.User), CancellationToken.None));

        conflict.Code.ShouldBe("login_taken");
    }
}