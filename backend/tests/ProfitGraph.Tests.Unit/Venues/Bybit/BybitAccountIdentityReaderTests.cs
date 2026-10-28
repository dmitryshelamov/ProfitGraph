using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The Bybit key's account read with <c>/v5/user/query-api</c> against the mock exchange (ADR 0049). Keys are invented and
///     nothing leaves the process; the key in the answer is never kept.
/// </summary>
public sealed class BybitAccountIdentityReaderTests
{
    private BybitGatewayHarness _harness = null!;
    private BybitAccountIdentityReader _reader = null!;

    [SetUp]
    public void Create()
    {
        _harness = new BybitGatewayHarness();
        _reader = new BybitAccountIdentityReader(_harness.ReadClient);
    }

    [Test]
    public async Task UidNoteAccessPermissionsAndExpiryAreRead()
    {
        _harness.Exchange.KeyCanWithdraw = true;

        AccountIdentityReadInternal read = await Read();

        AccountIdentityInternal identity = read.Identity.ShouldNotBeNull();
        identity.ShouldSatisfyAllConditions(
            () => identity.AccountId.ShouldBe("24617703"),
            () => identity.Name.ShouldBe("invented bot key"),
            () => identity.Kind.ShouldBe("main"),
            () => (identity.AccessLevel, identity.ReadOnly).ShouldBe(("read_write", false)),
            () => identity.Permissions.ShouldBe(["Spot:SpotTrade", "Wallet:AccountTransfer", "Wallet:Withdraw"]),
            () => identity.ExpiresAt.ShouldBe(new DateTimeOffset(year: 2027, month: 1, day: 24, hour: 0, minute: 0, second: 0, TimeSpan.Zero)));
        new[] { identity.AccountId, identity.Name, identity.Kind, identity.AccessLevel }.ShouldNotContain(MockBybitExchange.ApiKey);
        _harness.Exchange.Requests.ShouldContain((HttpMethod.Get, "/v5/user/query-api"));
    }

    [Test]
    public async Task ReadOnlyKeyIsDescribedSo()
    {
        _harness.Exchange.KeyReadOnly = true;

        AccountIdentityInternal identity = (await Read()).Identity.ShouldNotBeNull();

        (identity.AccessLevel, identity.ReadOnly).ShouldBe(("read_only", true));
    }

    [Test]
    public async Task RefusedKeyIsRejectedWithBybitsCode()
    {
        _harness.Exchange.KeyCode = 10003;

        AccountIdentityReadInternal read = await Read();

        read.ShouldBe(AccountIdentityReadInternal.Failed("venue_rejected", "10003"));
    }

    private Task<AccountIdentityReadInternal> Read()
    {
        return _reader.Read(
            new ExchangeCredentialSecretsInternal(Guid.NewGuid(), "bybit", MockBybitExchange.ApiKey, MockBybitExchange.ApiSecret),
            CancellationToken.None);
    }
}