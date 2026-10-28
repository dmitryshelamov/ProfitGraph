using Grpc.Core;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     The credential's brokerage account described from <c>GetAccounts</c> against the mock Invest API (ADR 0049): its ID,
///     name, kind and the token's access level; an account the token no longer sees and a refused call are failures.
/// </summary>
public sealed class TInvestAccountIdentityReaderTests
{
    private static readonly ExchangeCredentialSecretsInternal Credential = new ExchangeCredentialSecretsInternal(
        Guid.NewGuid(),
        "tinvest",
        new Dictionary<string, string> { [TInvestCredentialFields.Token] = "t.invented-token", [TInvestCredentialFields.AccountId] = "2000000002" });

    private MockTInvestApi _api = null!;

    [SetUp]
    public void CreateMock()
    {
        _api = new MockTInvestApi();
    }

    [Test]
    public async Task TheCredentialsAccountIsDescribedWithTheTokensAccess()
    {
        Accounts(
            Account("2000000001", AccountType.Tinkoff, AccessLevel.AccountAccessLevelFullAccess),
            Account("2000000002", AccountType.TinkoffIis, AccessLevel.AccountAccessLevelReadOnly));

        AccountIdentityReadInternal read = await Reader().Read(Credential, CancellationToken.None);

        AccountIdentityInternal identity = read.Identity.ShouldNotBeNull();
        identity.ShouldBe(new AccountIdentityInternal("2000000002", "Invented 2000000002", "iis", "read_only", ReadOnly: true, identity.Permissions, ExpiresAt: null));
        identity.Permissions.ShouldBeEmpty();
        _api.Calls.Single().Headers.GetValue("authorization").ShouldBe("Bearer t.invented-token");
    }

    [Test]
    public async Task AnAccountTheTokenNoLongerSeesAndARefusedCallAreFailures()
    {
        Accounts(Account("2000000001", AccountType.Tinkoff, AccessLevel.AccountAccessLevelFullAccess));
        (await Reader().Read(Credential, CancellationToken.None)).ShouldBe(AccountIdentityReadInternal.Failed("account_not_found"));

        _api.On<GetAccountsRequest, GetAccountsResponse>(UsersService.Descriptor, "GetAccounts", _ => throw MockTInvestApi.Refusal(StatusCode.Unauthenticated, "40003"));
        AccountIdentityReadInternal refused = await Reader().Read(Credential, CancellationToken.None);

        (refused.Identity, refused.FailureReason).ShouldBe((null, "venue_error"));
        refused.FailureDetail.ShouldNotBeNull().ShouldNotContain("t.invented-token");
    }

    private void Accounts(params Account[] accounts)
    {
        _api.On<GetAccountsRequest, GetAccountsResponse>(UsersService.Descriptor, "GetAccounts", _ => new GetAccountsResponse { Accounts = { accounts } });
    }

    private TInvestAccountIdentityReader Reader()
    {
        var clock = new FakeTimeProvider();

        return new TInvestAccountIdentityReader(
            new TInvestTransport(new MockTInvestInvokers(_api), new TInvestRequestLimiter(new TInvestRequestLimits(), clock), Options.Create(new TInvestOptions())));
    }

    private static Account Account(string id, AccountType type, AccessLevel access)
    {
        return new Account { Id = id, Name = "Invented " + id, Type = type, Status = AccountStatus.Open, AccessLevel = access };
    }
}