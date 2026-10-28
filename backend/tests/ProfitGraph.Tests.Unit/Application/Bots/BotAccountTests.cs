using NSubstitute;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotAccount;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.BalanceStatuses;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Accounts.Identities;
using ProfitGraph.Domain.Accounts.Snapshots;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The account summary of the bot page (ADR 0049): stored data only, another user's bot is missing, an account without a
///     snapshot or with a failed read says so, a credential whose public identifier is its API key never shows it, and only
///     the other running bots of the same key are counted.
/// </summary>
public sealed class BotAccountTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 28, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly UserId Owner = UserId.New();
    private static readonly VenueId Bybit = new VenueId("bybit");
    private static readonly VenueId Steam = new VenueId("steam");

    private IBotRepository _bots = null!;
    private IExchangeCredentialRepository _credentials = null!;
    private IAccountBalanceStatusRepository _statuses = null!;
    private IAccountSnapshotRepository _snapshots = null!;
    private IAccountIdentityRepository _identities = null!;
    private IVenueAccountStatus _venueStatus = null!;
    private IVenueAccountProtection _protection = null!;

    [SetUp]
    public void CreateStores()
    {
        _bots = Substitute.For<IBotRepository>();
        _credentials = Substitute.For<IExchangeCredentialRepository>();
        _statuses = Substitute.For<IAccountBalanceStatusRepository>();
        _snapshots = Substitute.For<IAccountSnapshotRepository>();
        _identities = Substitute.For<IAccountIdentityRepository>();
        _venueStatus = Substitute.For<IVenueAccountStatus>();
        _protection = Substitute.For<IVenueAccountProtection>();
    }

    [Test]
    public async Task SummaryHasTheKeyTheLatestSnapshotTheIdentityAndTheOtherRunningBotsOfTheKey()
    {
        ExchangeCredential credential = Credential(Bybit, "bybit-public-api-key");
        Bot bot = Bot(credential, allocationCap: 500m);
        Bot otherRunning = Bot(credential, allocationCap: 250m, running: true);
        Bot otherStopped = Bot(credential, allocationCap: 75m);
        Bot otherKey = Bot(Credential(Bybit, publicId: null), allocationCap: 900m, running: true);
        _bots.ListByOwner(Owner, Arg.Any<CancellationToken>()).Returns([bot, otherRunning, otherStopped, otherKey]);
        var status = AccountBalanceStatus.New(credential.Id);
        status.RecordSuccess(Now);
        _statuses.Get(credential.Id, Arg.Any<CancellationToken>()).Returns(status);
        _snapshots.Latest(credential.Id, Arg.Any<CancellationToken>())
            .Returns(
                AccountSnapshot.Capture(
                    AccountSnapshotId.New(),
                    credential.Id,
                    Now,
                    totalValue: 1234.5m,
                    "USD",
                    availableValue: 1000m,
                    [new AccountCoinBalance("USDT", Balance: 1000m, UsdValue: 1000m)]));
        var identity = AccountIdentity.New(credential.Id);
        identity.RecordRead(new AccountIdentityDetails("123456", "bot key", "main", "read_write", readOnly: false, ["Spot:SpotTrade"], expiresAt: null), Now);
        _identities.Get(credential.Id, Arg.Any<CancellationToken>()).Returns(identity);

        BotAccountInternal account = await Handle(bot);

        account.ShouldSatisfyAllConditions(
            () => (account.CredentialId, account.Venue, account.CredentialLabel, account.ApiKeyLast4).ShouldBe((credential.Id.Value, "bybit", "main key", "real")),
            () => account.AccountPublicId.ShouldBeNull(),
            () => account.Snapshot.ShouldNotBeNull().TotalValue.ShouldBe(1234.5m),
            () => account.Snapshot!.Coins.ShouldBe([new AccountCoinBalanceInternal("USDT", Balance: 1000m, UsdValue: 1000m)]),
            () => (account.LastSuccessAt, account.FailureReason).ShouldBe((Now, null)),
            () => account.Identity.ShouldBe(
                new AccountIdentityInternal("123456", "bot key", "main", "read_write", ReadOnly: false, account.Identity!.Permissions, ExpiresAt: null)),
            () => account.Identity!.Permissions.ShouldBe(["Spot:SpotTrade"]),
            () => account.IdentityReadAt.ShouldBe(Now),
            () => account.VenueStatus.ShouldBeNull(),
            () => account.Protection.ShouldBe(VenueAccountProtectionInternal.Clear),
            () => account.AllocationCap.ShouldBe(500m),
            () => account.OtherRunningBots.ShouldBe(
                [new BotAllocationInternal(otherRunning.Id.Value, Name: null, "BTCUSDT", AllocationCap: 250m, BotRuntimeStatus.Created)]));
        await _snapshots.DidNotReceiveWithAnyArgs().List(credentialId: default, from: default, to: default, limit: default, cancellationToken: default);
    }

    [Test]
    public async Task AccountWithoutASnapshotOrIdentityShowsTheFailedReadAndTheVenuesOwnState()
    {
        ExchangeCredential credential = Credential(Steam, "76561190000000001");
        Bot bot = Bot(credential, allocationCap: 10m);
        _bots.ListByOwner(Owner, Arg.Any<CancellationToken>()).Returns([bot]);
        var status = AccountBalanceStatus.New(credential.Id);
        status.RecordFailure("venue_unavailable", "503", Now);
        _statuses.Get(credential.Id, Arg.Any<CancellationToken>()).Returns(status);
        _venueStatus.Read(credential.Id.Value, Arg.Any<CancellationToken>()).Returns(new VenueAccountStatusInternal("Ready", NextCheckAt: null));
        var protection = new VenueAccountProtectionInternal(Open: true, "Captcha", Now, AutomaticResumeAt: null);
        _protection.Read(credential.Id.Value, Arg.Any<CancellationToken>()).Returns(protection);

        BotAccountInternal account = await Handle(bot);

        account.ShouldSatisfyAllConditions(
            () => account.AccountPublicId.ShouldBe("76561190000000001"),
            () => account.Snapshot.ShouldBeNull(),
            () => (account.LastSuccessAt, account.FailureReason, account.NextAttemptAt).ShouldBe((null, "venue_unavailable", Now.AddMinutes(5))),
            () => (account.Identity, account.IdentityReadAt, account.IdentityFailureReason).ShouldBe((null, null, null)),
            () => account.VenueStatus.ShouldBe(new VenueAccountStatusInternal("Ready", NextCheckAt: null)),
            () => account.Protection.ShouldBe(protection),
            () => account.OtherRunningBots.ShouldBeEmpty());
    }

    [Test]
    public async Task AnotherUsersBotIsMissing()
    {
        Bot bot = Bot(Credential(Bybit, publicId: null), allocationCap: 1m);

        (await Should.ThrowAsync<ResourceNotFoundException>(() => Handler().Handle(new GetBotAccountQuery(Guid.NewGuid(), bot.Id.Value), CancellationToken.None)))
            .Code.ShouldBe("bot_not_found");
        await _credentials.DidNotReceiveWithAnyArgs().Get(id: default, cancellationToken: default);
    }

    private Task<BotAccountInternal> Handle(Bot bot)
    {
        return Handler().Handle(new GetBotAccountQuery(Owner.Value, bot.Id.Value), CancellationToken.None);
    }

    private GetBotAccountQueryHandler Handler()
    {
        _venueStatus.Venue.Returns(Steam);
        _protection.Venue.Returns(Steam);

        return new GetBotAccountQueryHandler(
            _bots,
            _credentials,
            _statuses,
            _snapshots,
            _identities,
            new VenuePorts<IVenueCredentialSchema>([Schema(Bybit, "apiKey"), Schema(Steam, "steamId")]),
            new VenuePorts<IVenueAccountStatus>([_venueStatus]),
            new VenuePorts<IVenueAccountProtection>([_protection]));
    }

    private ExchangeCredential Credential(VenueId venue, string? publicId)
    {
        var credential = ExchangeCredential.Create(
            ExchangeCredentialId.New(),
            Owner,
            venue,
            new ExchangeCredentialLabel("main key"),
            new ApiKeyLast4("real"),
            new Dictionary<string, EncryptedSecret> { ["apiKey"] = new EncryptedSecret("k1", new byte[12], [1], new byte[16]) },
            Now,
            publicId: publicId is null ? null : new CredentialPublicId(publicId));
        _credentials.Get(credential.Id, Arg.Any<CancellationToken>()).Returns(credential);

        return credential;
    }

    private Bot Bot(ExchangeCredential credential, decimal allocationCap, bool running = false)
    {
        var bot = ProfitGraph.Domain.Bots.Bot.Create(
            BotId.New(),
            Owner,
            StrategyId.New(),
            StrategyVersionId.New(),
            credential.Id,
            new CandleSeries(credential.Venue, new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h")),
            new BotParameters(new Dictionary<string, decimal>()),
            allocationCap,
            new BotRiskLimits(maxOrderQuote: 1m, dailyLossLimitQuote: 1m),
            new BotFeeRates(limitRate: 0.001m, marketRate: 0.001m, Now),
            Now);

        if (running)
        {
            bot.Start(bot.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, Now);
        }

        _bots.Get(bot.Id, Arg.Any<CancellationToken>()).Returns(bot);

        return bot;
    }

    private static IVenueCredentialSchema Schema(VenueId venue, string publicField)
    {
        var schema = Substitute.For<IVenueCredentialSchema>();
        schema.Venue.Returns(venue);
        schema.PublicField.Returns(publicField);

        return schema;
    }
}