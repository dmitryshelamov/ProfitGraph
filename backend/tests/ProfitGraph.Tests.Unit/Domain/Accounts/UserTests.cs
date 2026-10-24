using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Tests.Unit.Domain.Accounts;

public sealed class UserTests
{
    private static readonly DateTimeOffset Created = new DateTimeOffset(year: 2026, month: 10, day: 24, hour: 10, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddHours(1);

    [Test]
    public void CreateStartsActive()
    {
        var id = UserId.New();
        var user = User.Create(id, new UserLogin("alice"), UserRole.User, Created);

        user.ShouldSatisfyAllConditions(
            () => user.Id.ShouldBe(id),
            () => user.Login.ShouldBe(new UserLogin("alice")),
            () => user.IsActive.ShouldBeTrue(),
            () => user.CreatedAt.ShouldBe(Created));
    }

    [Test]
    public void OwnerBlocksAnotherUser()
    {
        User user = Active(UserRole.User);

        user.Block(UserId.New(), activeOwnerCount: 1, Later);

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

        Should.Throw<AccountRuleException>(() => owner.Block(UserId.New(), activeOwnerCount: 1, Later)).Code.ShouldBe(AccountRuleException.LastOwner);
        owner.IsActive.ShouldBeTrue();
    }

    [Test]
    public void OwnerCanBeBlockedWhileAnotherOwnerRemains()
    {
        User owner = Active(UserRole.Owner);

        owner.Block(UserId.New(), activeOwnerCount: 2, Later);

        owner.Status.ShouldBe(UserStatus.Blocked);
    }

    [Test]
    public void BlockingABlockedAccountChangesNothing()
    {
        User user = Active(UserRole.User);
        user.Block(UserId.New(), activeOwnerCount: 1, Later);

        user.Block(UserId.New(), activeOwnerCount: 1, Later.AddHours(1));

        user.Status.ShouldBe(UserStatus.Blocked);
        user.UpdatedAt.ShouldBe(Later);
    }

    [Test]
    public void RestoreRejectsAMissingPasswordHash()
    {
        Should.Throw<ArgumentException>(() => User.Restore(UserId.New(), new UserLogin("alice"), " ", UserRole.User, UserStatus.Active, Created, Created));
    }

    private static User Active(UserRole role)
    {
        var user = User.Create(UserId.New(), new UserLogin("account"), role, Created);
        user.SetPasswordHash("hash", Created);

        return user;
    }
}