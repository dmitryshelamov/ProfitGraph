using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Tests.Unit.Domain.Accounts;

public sealed class UserTests
{
    private static readonly DateTimeOffset Created = new DateTimeOffset(year: 2026, month: 10, day: 24, hour: 10, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddHours(1);

    [Test]
    public void CreateTrimsTheLoginAndStartsActive()
    {
        var user = User.Create(Guid.NewGuid(), "  alice  ", UserRole.User, Created);

        user.Login.ShouldBe("alice");
        user.IsActive.ShouldBeTrue();
        user.CreatedAt.ShouldBe(Created);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateRejectsAnEmptyLogin(string login)
    {
        Should.Throw<AccountRuleException>(() => User.Create(Guid.NewGuid(), login, UserRole.User, Created)).Code.ShouldBe(AccountRuleException.InvalidLogin);
    }

    [Test]
    public void CreateRejectsATooLongLogin()
    {
        Should.Throw<AccountRuleException>(() => User.Create(Guid.NewGuid(), new string(c: 'a', User.MaxLoginLength + 1), UserRole.User, Created));
    }

    [Test]
    public void OwnerBlocksAnotherUser()
    {
        User user = Active(UserRole.User);

        user.Block(Guid.NewGuid(), activeOwnerCount: 1, Later);

        user.Status.ShouldBe(UserStatus.Blocked);
        user.UpdatedAt.ShouldBe(Later);
    }

    [Test]
    public void AccountCannotBlockItself()
    {
        User owner = Active(UserRole.Owner);

        Should.Throw<AccountRuleException>(() => owner.Block(owner.Id, activeOwnerCount: 2, Later)).Code.ShouldBe(AccountRuleException.CannotBlockSelf);
        owner.IsActive.ShouldBeTrue();
    }

    [Test]
    public void LastActiveOwnerCannotBeBlocked()
    {
        User owner = Active(UserRole.Owner);

        Should.Throw<AccountRuleException>(() => owner.Block(Guid.NewGuid(), activeOwnerCount: 1, Later)).Code.ShouldBe(AccountRuleException.LastOwner);
        owner.IsActive.ShouldBeTrue();
    }

    [Test]
    public void OwnerCanBeBlockedWhileAnotherOwnerRemains()
    {
        User owner = Active(UserRole.Owner);

        owner.Block(Guid.NewGuid(), activeOwnerCount: 2, Later);

        owner.Status.ShouldBe(UserStatus.Blocked);
    }

    [Test]
    public void BlockingABlockedAccountChangesNothing()
    {
        User user = Active(UserRole.User);
        user.Block(Guid.NewGuid(), activeOwnerCount: 1, Later);

        user.Block(Guid.NewGuid(), activeOwnerCount: 1, Later.AddHours(1));

        user.Status.ShouldBe(UserStatus.Blocked);
        user.UpdatedAt.ShouldBe(Later);
    }

    [Test]
    public void RestoreRejectsAMissingPasswordHash()
    {
        Should.Throw<ArgumentException>(() => User.Restore(Guid.NewGuid(), "alice", " ", UserRole.User, UserStatus.Active, Created, Created));
    }

    private static User Active(UserRole role)
    {
        var user = User.Create(Guid.NewGuid(), "account", role, Created);
        user.SetPasswordHash("hash", Created);

        return user;
    }
}