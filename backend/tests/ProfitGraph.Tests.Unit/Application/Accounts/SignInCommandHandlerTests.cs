using NSubstitute;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.SignIn;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Unit.Common;

namespace ProfitGraph.Tests.Unit.Application.Accounts;

public sealed class SignInCommandHandlerTests
{
    private static readonly string[] InvalidLogins = ["   ", new string(c: 'a', UserLogin.MaxLength + 1)];

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
        UserInternal? result = await Handler().Handle(new SignInCommand("ghost", "password-123456"), CancellationToken.None);

        result.ShouldBeNull();
        _passwords.Received(1).VerifyDummy("password-123456");
    }

    [TestCaseSource(nameof(InvalidLogins))]
    public async Task InvalidLoginFailsWithoutALookup(string login)
    {
        UserInternal? result = await Handler().Handle(new SignInCommand(login, "password-123456"), CancellationToken.None);

        result.ShouldBeNull();
        _passwords.Received(1).VerifyDummy("password-123456");
        await _users.DidNotReceiveWithAnyArgs().GetByLogin(null!, CancellationToken.None);
    }

    [Test]
    public async Task BlockedAccountFailsLikeAnUnknownLogin()
    {
        User user = Stored("alice");
        user.Block(UserId.New(), activeOwnerCount: 1, _clock.GetUtcNow());
        _users.GetByLogin(new UserLogin("alice"), Arg.Any<CancellationToken>()).Returns(user);

        UserInternal? result = await Handler().Handle(new SignInCommand("alice", "password-123456"), CancellationToken.None);

        result.ShouldBeNull();
        _passwords.Received(1).VerifyDummy("password-123456");
        _passwords.DidNotReceiveWithAnyArgs().Verify(null!, null!);
    }

    [Test]
    public async Task WrongPasswordFails()
    {
        User user = Stored("alice");
        _users.GetByLogin(new UserLogin("alice"), Arg.Any<CancellationToken>()).Returns(user);
        _passwords.Verify(user, "wrong").Returns(PasswordCheck.Failed);

        UserInternal? result = await Handler().Handle(new SignInCommand(" alice ", "wrong"), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Test]
    public async Task ValidPasswordSignsInWithoutRewritingTheHash()
    {
        User user = Stored("alice");
        _users.GetByLogin(new UserLogin("alice"), Arg.Any<CancellationToken>()).Returns(user);
        _passwords.Verify(user, "password-123456").Returns(PasswordCheck.Valid);

        UserInternal? result = await Handler().Handle(new SignInCommand("alice", "password-123456"), CancellationToken.None);

        result.ShouldNotBeNull().Id.ShouldBe(user.Id.Value);
        await _users.DidNotReceiveWithAnyArgs().Update(null!, CancellationToken.None);
    }

    [Test]
    public async Task OutdatedHashIsReplacedOnSuccessfulSignIn()
    {
        User user = Stored("alice");
        _users.GetByLogin(new UserLogin("alice"), Arg.Any<CancellationToken>()).Returns(user);
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
        var user = User.Create(UserId.New(), new UserLogin(login), UserRole.User, _clock.GetUtcNow());
        user.SetPasswordHash("old-hash", _clock.GetUtcNow());

        return user;
    }
}