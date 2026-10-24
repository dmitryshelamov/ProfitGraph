using NSubstitute;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.SignIn;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Unit.Common;

namespace ProfitGraph.Tests.Unit.Application.Accounts;

public sealed class SignInCommandHandlerTests
{
    private readonly FixedClock _clock = new FixedClock(new DateTimeOffset(year: 2026, month: 10, day: 24, hour: 12, minute: 0, second: 0, TimeSpan.Zero));
    private IPasswordHasher _passwords = null!;
    private IUserRepository _users = null!;

    [SetUp]
    public void CreateSubstitutes()
    {
        _passwords = Substitute.For<IPasswordHasher>();
        _users = Substitute.For<IUserRepository>();
    }

    [Test]
    public async Task UnknownLoginFailsAfterADummyVerification()
    {
        UserContract? result = await Handler().Handle(new SignInCommand("ghost", "password-123456"), CancellationToken.None);

        result.ShouldBeNull();
        _passwords.Received(1).VerifyDummy("password-123456");
    }

    [Test]
    public async Task BlockedAccountFailsLikeAnUnknownLogin()
    {
        User user = Stored("alice");
        user.Block(Guid.NewGuid(), activeOwnerCount: 1, _clock.GetUtcNow());
        _users.GetByLogin("alice", Arg.Any<CancellationToken>()).Returns(user);

        UserContract? result = await Handler().Handle(new SignInCommand("alice", "password-123456"), CancellationToken.None);

        result.ShouldBeNull();
        _passwords.Received(1).VerifyDummy("password-123456");
        _passwords.DidNotReceiveWithAnyArgs().Verify(null!, null!);
    }

    [Test]
    public async Task WrongPasswordFails()
    {
        User user = Stored("alice");
        _users.GetByLogin("alice", Arg.Any<CancellationToken>()).Returns(user);
        _passwords.Verify(user, "wrong").Returns(PasswordCheck.Failed);

        UserContract? result = await Handler().Handle(new SignInCommand(" alice ", "wrong"), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Test]
    public async Task ValidPasswordSignsInWithoutRewritingTheHash()
    {
        User user = Stored("alice");
        _users.GetByLogin("alice", Arg.Any<CancellationToken>()).Returns(user);
        _passwords.Verify(user, "password-123456").Returns(PasswordCheck.Valid);

        UserContract? result = await Handler().Handle(new SignInCommand("alice", "password-123456"), CancellationToken.None);

        result.ShouldNotBeNull().Id.ShouldBe(user.Id);
        await _users.DidNotReceiveWithAnyArgs().Update(null!, CancellationToken.None);
    }

    [Test]
    public async Task OutdatedHashIsReplacedOnSuccessfulSignIn()
    {
        User user = Stored("alice");
        _users.GetByLogin("alice", Arg.Any<CancellationToken>()).Returns(user);
        _passwords.Verify(user, "password-123456").Returns(PasswordCheck.RehashNeeded);
        _passwords.Hash(user, "password-123456").Returns("new-hash");

        await Handler().Handle(new SignInCommand("alice", "password-123456"), CancellationToken.None);

        user.PasswordHash.ShouldBe("new-hash");
        await _users.Received(1).Update(user, Arg.Any<CancellationToken>());
    }

    private SignInCommandHandler Handler()
    {
        return new SignInCommandHandler(_users, _passwords, _clock);
    }

    private User Stored(string login)
    {
        var user = User.Create(Guid.NewGuid(), login, UserRole.User, _clock.GetUtcNow());
        user.SetPasswordHash("old-hash", _clock.GetUtcNow());

        return user;
    }
}