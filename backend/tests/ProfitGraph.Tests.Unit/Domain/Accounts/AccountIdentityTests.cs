using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Tests.Unit.Domain.Accounts;

/// <summary>
///     The cached identity of a credential's account (ADR 0049): a success is read again after a day, a failure after an hour,
///     and a failure never discards the last details.
/// </summary>
public sealed class AccountIdentityTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 28, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void ReadStoresTheDetailsAndIsRepeatedAfterADay()
    {
        var identity = AccountIdentity.New(ExchangeCredentialId.New());
        AccountIdentityDetails details = Details();

        identity.RecordRead(details, Now);

        identity.ShouldSatisfyAllConditions(
            () => identity.Details.ShouldBe(details),
            () => identity.ReadAt.ShouldBe(Now),
            () => identity.LastAttemptAt.ShouldBe(Now),
            () => identity.FailureReason.ShouldBeNull(),
            () => identity.NextAttemptAt.ShouldBe(Now.AddDays(1)));
    }

    [Test]
    public void FailureKeepsTheLastDetailsAndRetriesAfterAnHour_AndASuccessClearsIt()
    {
        var identity = AccountIdentity.New(ExchangeCredentialId.New());
        identity.RecordRead(Details(), Now);

        identity.RecordFailure("venue_unavailable", new string(c: 'x', count: 300), Now.AddDays(1));

        identity.ShouldSatisfyAllConditions(
            () => identity.Details.ShouldBe(Details()),
            () => identity.ReadAt.ShouldBe(Now),
            () => identity.LastAttemptAt.ShouldBe(Now.AddDays(1)),
            () => identity.FailureReason.ShouldBe("venue_unavailable"),
            () => identity.FailureDetail!.Length.ShouldBe(200),
            () => identity.NextAttemptAt.ShouldBe(Now.AddDays(1).AddHours(1)));
        identity.RecordRead(Details(), Now.AddDays(2));
        (identity.FailureReason, identity.FailureDetail, identity.ReadAt).ShouldBe((null, null, Now.AddDays(2)));
    }

    [Test]
    public void FailureNeedsAReason()
    {
        Should.Throw<ArgumentException>(() => AccountIdentity.New(ExchangeCredentialId.New()).RecordFailure(" ", detail: null, Now));
    }

    [Test]
    public void DetailsAreTrimmedShortenedAndOrdered_AndUnusablePermissionsAreRefused()
    {
        var details = new AccountIdentityDetails(
            " 123 ",
            new string(c: 'n', count: 200),
            string.Empty,
            "read_write",
            readOnly: false,
            ["Spot:SpotTrade", "ContractTrade:Order", "Spot:SpotTrade"],
            Now.ToOffset(TimeSpan.FromHours(3)));

        details.ShouldSatisfyAllConditions(
            () => details.AccountId.ShouldBe("123"),
            () => details.Name!.Length.ShouldBe(AccountIdentityDetails.MaxTextLength),
            () => details.Kind.ShouldBeNull(),
            () => details.Permissions.ShouldBe(["ContractTrade:Order", "Spot:SpotTrade"]),
            () => details.ExpiresAt!.Value.Offset.ShouldBe(TimeSpan.Zero));
        Should.Throw<AccountRuleException>(() => new AccountIdentityDetails(
                accountId: null,
                name: null,
                kind: null,
                accessLevel: null,
                readOnly: null,
                ["Spot Trade"],
                expiresAt: null))
            .Code.ShouldBe(AccountRuleException.InvalidAccountIdentity);
    }

    [Test]
    public void RestoredDetailsNeedTheTimeTheyWereRead()
    {
        Should.Throw<ArgumentException>(() => AccountIdentity.Restore(
            ExchangeCredentialId.New(),
            Details(),
            readAt: null,
            Now,
            failureReason: null,
            failureDetail: null,
            nextAttemptAt: null));
    }

    private static AccountIdentityDetails Details()
    {
        return new AccountIdentityDetails("123456", "bot key", "main", "read_write", readOnly: false, ["Spot:SpotTrade"], Now.AddDays(90));
    }
}