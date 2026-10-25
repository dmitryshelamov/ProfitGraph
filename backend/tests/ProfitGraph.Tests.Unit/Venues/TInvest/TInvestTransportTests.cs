using Grpc.Core;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     One Invest API call: token and app name only as metadata, a deadline, broker codes read exactly, and a group held
///     back after the broker reports its limit exhausted.
/// </summary>
public sealed class TInvestTransportTests
{
    private static readonly TInvestToken Token = new TInvestToken("t.invented-token");
    private FakeTimeProvider _clock = null!;
    private MockTInvestApi _api = null!;

    [SetUp]
    public void CreateMock()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(year: 2026, month: 10, day: 26, hour: 7, minute: 0, second: 0, TimeSpan.Zero));
        _api = new MockTInvestApi();
    }

    [Test]
    public async Task TheTokenTravelsAsBearerMetadata_AndPrintsRedacted()
    {
        _api.On<GetAccountsRequest, GetAccountsResponse>(UsersService.Descriptor, "GetAccounts", _ => new GetAccountsResponse());

        await Accounts(Transport());

        (string method, Metadata headers, DateTime? deadline) = _api.Calls.Single();
        method.ShouldBe(MockTInvestApi.Name(UsersService.Descriptor, "GetAccounts"));
        headers.GetValue("authorization").ShouldBe("Bearer t.invented-token");
        headers.GetValue("x-app-name").ShouldBe("ProfitGraph");
        deadline.ShouldNotBeNull();
        Token.ToString().ShouldBe("[redacted]");
        Token.Fingerprint.ShouldNotContain("invented");
    }

    [TestCase(StatusCode.InvalidArgument, "30079", "Rejected", 30079)]
    [TestCase(StatusCode.InvalidArgument, "price is out of limits", "Unknown", null)]
    [TestCase(StatusCode.InvalidArgument, "30079 price", "Unknown", null)]
    [TestCase(StatusCode.Unauthenticated, "40003", "Unauthenticated", 40003)]
    [TestCase(StatusCode.PermissionDenied, "", "PermissionDenied", null)]
    [TestCase(StatusCode.DeadlineExceeded, "", "Timeout", null)]
    [TestCase(StatusCode.Unavailable, "", "Unavailable", null)]
    public async Task AFailureIsClassified_AndOnlyAnExactNumberIsABrokerCode(StatusCode status, string detail, string kind, int? code)
    {
        _api.On<GetAccountsRequest, GetAccountsResponse>(UsersService.Descriptor, "GetAccounts", _ => throw MockTInvestApi.Refusal(status, detail));

        var error = await Should.ThrowAsync<TInvestRpcException>(() => Accounts(Transport()));

        error.Kind.ToString().ShouldBe(kind);
        error.BrokerCode.ShouldBe(code);
        error.Message.ShouldNotContain("limits");
    }

    [Test]
    public async Task AnExhaustedLimitHoldsTheGroupBackUntilTheReportedReset()
    {
        var refuse = true;
        _api.On<GetAccountsRequest, GetAccountsResponse>(
            UsersService.Descriptor,
            "GetAccounts",
            _ => refuse
                ? throw MockTInvestApi.Refusal(StatusCode.ResourceExhausted, "80002", [new Metadata.Entry("x-ratelimit-reset", "20")])
                : new GetAccountsResponse());
        TInvestTransport transport = Transport();

        (await Should.ThrowAsync<TInvestRpcException>(() => Accounts(transport))).Kind.ShouldBe(TInvestFailureKind.RateLimited);
        refuse = false;
        Task waiting = Accounts(transport);
        await Task.Delay(millisecondsDelay: 50);
        waiting.IsCompleted.ShouldBeFalse();
        _api.Calls.Count.ShouldBe(1);

        _clock.Advance(TimeSpan.FromSeconds(20));
        await waiting;

        _api.Calls.Count.ShouldBe(2);
    }

    [Test]
    public async Task AGroupAdmitsItsLimitPerMinute_AndOtherTokensAreSeparate()
    {
        _api.On<GetAccountsRequest, GetAccountsResponse>(UsersService.Descriptor, "GetAccounts", _ => new GetAccountsResponse());
        TInvestTransport transport = Transport(new TInvestRequestLimits { Users = 2 });

        await Accounts(transport);
        await Accounts(transport);
        Task third = Accounts(transport);
        await Accounts(transport, new TInvestToken("t.another-invented-token"));
        await Task.Delay(millisecondsDelay: 50);

        third.IsCompleted.ShouldBeFalse();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await third;
        _api.Calls.Count.ShouldBe(4);
    }

    [Test]
    public void ADisabledVenueSendsNothing()
    {
        var invokers = new TInvestChannelInvokers(Options.Create(new TInvestOptions()), _clock);

        Should.Throw<TInvestRpcException>(() => invokers.Get()).Kind.ShouldBe(TInvestFailureKind.Unavailable);
    }

    private TInvestTransport Transport(TInvestRequestLimits? limits = null)
    {
        return new TInvestTransport(
            new MockTInvestInvokers(_api),
            new TInvestRequestLimiter(limits ?? new TInvestRequestLimits(), _clock),
            Options.Create(new TInvestOptions()));
    }

    private static Task<GetAccountsResponse> Accounts(TInvestTransport transport, TInvestToken? token = null)
    {
        return transport.Unary(
            token ?? Token,
            TInvestMethodGroup.Users,
            (invoker, options) => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest(), options),
            CancellationToken.None);
    }
}