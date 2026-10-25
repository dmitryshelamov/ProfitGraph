using Grpc.Core;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     The accounts a token sees: only an open brokerage or IIS account with full access is eligible (ADR 0023).
/// </summary>
public sealed class TInvestAccountDirectoryTests
{
    private static readonly Dictionary<string, string> Fields = new Dictionary<string, string> { [TInvestCredentialFields.Token] = "t.invented-token" };
    private MockTInvestApi _api = null!;

    [SetUp]
    public void CreateMock()
    {
        _api = new MockTInvestApi();
    }

    [Test]
    public async Task OnlyAnOpenBrokerageOrIisAccountWithFullAccessIsEligible()
    {
        _api.On<GetAccountsRequest, GetAccountsResponse>(
            UsersService.Descriptor,
            "GetAccounts",
            _ => new GetAccountsResponse
            {
                Accounts =
                {
                    Account("2000000001", AccountType.Tinkoff, AccountStatus.Open, AccessLevel.AccountAccessLevelFullAccess),
                    Account("2000000002", AccountType.TinkoffIis, AccountStatus.Open, AccessLevel.AccountAccessLevelFullAccess),
                    Account("2000000003", AccountType.Tinkoff, AccountStatus.Open, AccessLevel.AccountAccessLevelReadOnly),
                    Account("2000000004", AccountType.Tinkoff, AccountStatus.Closed, AccessLevel.AccountAccessLevelFullAccess),
                    Account("2000000005", AccountType.InvestBox, AccountStatus.Open, AccessLevel.AccountAccessLevelFullAccess)
                }
            });

        IReadOnlyList<VenueAccountInternal> accounts = await Directory().List(Fields, CancellationToken.None);

        accounts.Select(account => (account.Id, account.Kind, account.Eligible, account.Ineligibility))
            .ShouldBe(
            [
                ("2000000001", "broker", true, null),
                ("2000000002", "iis", true, null),
                ("2000000003", "broker", false, "not_full_access"),
                ("2000000004", "broker", false, "not_open"),
                ("2000000005", "other", false, "unsupported_kind")
            ]);
        _api.Calls.Single().Headers.GetValue("authorization").ShouldBe("Bearer t.invented-token");
    }

    [Test]
    public async Task ARefusedTokenIsAValidationError_AndOtherFailuresAreUnavailability()
    {
        _api.On<GetAccountsRequest, GetAccountsResponse>(UsersService.Descriptor, "GetAccounts", _ => throw MockTInvestApi.Refusal(StatusCode.Unauthenticated, "40003"));
        (await Should.ThrowAsync<ApplicationValidationException>(() => Directory().List(Fields, CancellationToken.None))).Errors.Single()
            .Code.ShouldBe("invalid_venue_credential");

        _api.On<GetAccountsRequest, GetAccountsResponse>(UsersService.Descriptor, "GetAccounts", _ => throw MockTInvestApi.Refusal(StatusCode.Unavailable, string.Empty));
        await Should.ThrowAsync<VenueAccountsUnavailableException>(() => Directory().List(Fields, CancellationToken.None));
    }

    private TInvestAccountDirectory Directory()
    {
        var clock = new FakeTimeProvider();

        return new TInvestAccountDirectory(
            new TInvestTransport(new MockTInvestInvokers(_api), new TInvestRequestLimiter(new TInvestRequestLimits(), clock), Options.Create(new TInvestOptions())));
    }

    private static Account Account(string id, AccountType type, AccountStatus status, AccessLevel access)
    {
        return new Account { Id = id, Name = "Invented " + id, Type = type, Status = status, AccessLevel = access };
    }
}