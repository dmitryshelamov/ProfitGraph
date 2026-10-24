using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Tests.Unit.Domain.Accounts;

public sealed class UserLoginTests
{
    [Test]
    public void LoginIsTrimmedAndKeepsItsCase()
    {
        new UserLogin("  Alice  ").Value.ShouldBe("Alice");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void EmptyLoginIsRejected(string? value)
    {
        Should.Throw<AccountRuleException>(() => new UserLogin(value)).Code.ShouldBe(AccountRuleException.InvalidLogin);
        UserLogin.TryCreate(value, out UserLogin? login).ShouldBeFalse();
        login.ShouldBeNull();
    }

    [Test]
    public void LoginFitsTheLimit()
    {
        UserLogin.TryCreate(new string(c: 'a', UserLogin.MaxLength), out _).ShouldBeTrue();
        UserLogin.TryCreate(new string(c: 'a', UserLogin.MaxLength + 1), out _).ShouldBeFalse();
        Should.Throw<AccountRuleException>(() => new UserLogin(new string(c: 'a', UserLogin.MaxLength + 1)));
    }

    [Test]
    public void TryCreateReturnsTheTrimmedLogin()
    {
        UserLogin.TryCreate(" alice ", out UserLogin? login).ShouldBeTrue();
        login.ShouldBe(new UserLogin("alice"));
    }
}