using System.Net;
using System.Text;

using Microsoft.Extensions.Options;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Protection;
using ProfitGraph.Venues.Steam.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Steam buy orders against the mock: journaled before sending, one buy order per item, never a second POST for a write
///     whose outcome is unknown (ADR 0022).
/// </summary>
public sealed class SteamBuyOrdersTests
{
    private const string Item = "Dreams & Nightmares Case";
    private const string CreatePath = "/market/createbuyorder/";

    private static readonly Guid Bot = Guid.NewGuid();

    private SteamSessionHarness _harness = null!;
    private InMemorySteamOrderJournal _journal = null!;
    private SteamOrderUpdates _updates = null!;
    private RecordingSteamOrderWaiting _waiting = null!;
    private SteamBotSettings _settings = null!;
    private SellerFeeSchedule? _fees;

    [SetUp]
    public void SetUp()
    {
        _harness = new SteamSessionHarness { Options = Options() };
        _settings = SteamBotSettings.Defaults;
        _fees = null;
        _journal = new InMemorySteamOrderJournal();
        _updates = new SteamOrderUpdates();
        _waiting = new RecordingSteamOrderWaiting();
    }

    [Test]
    public async Task ALimitBuyIsJournaledThenPlacedWithSteamsFormFields()
    {
        SteamBuyOrders orders = Orders();
        int postsBeforeMark = -1;

        ExternalOrderUpdate? rejection = await orders.Submit(
            Account(),
            "pg-1",
            Limit(id: 1, quantity: 3m, price: 512.34m),
            _ =>
            {
                postsBeforeMark = _harness.Steam.Count(CreatePath);
                _journal.All.Single().State.ShouldBe(SteamOrderJournalState.Placing);

                return Task.CompletedTask;
            },
            CancellationToken.None);

        rejection.ShouldBeNull();
        postsBeforeMark.ShouldBe(0);
        _harness.Steam.Count(CreatePath).ShouldBe(1);
        _harness.Steam.LastBuyForm.ShouldSatisfyAllConditions(
            form => form["appid"].ShouldBe("730"),
            form => form["market_hash_name"].ShouldBe(Item),
            form => form["quantity"].ShouldBe("3"),
            form => form["price_total"].ShouldBe("153702"),
            form => form["confirmation"].ShouldBe("0"),
            form => form["billing_country"].ShouldBeEmpty(),
            form => form.ShouldContainKey("sessionid"));
        SteamOrderJournalEntry entry = _journal.All.Single();
        entry.State.ShouldBe(SteamOrderJournalState.Active);
        entry.SteamOrderId.ShouldBe("9001");
        (await Published()).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1)]);
    }

    [Test]
    public async Task ASecondBuyOfTheItemWaitsForTheSlotAndGoesOnce()
    {
        SteamBuyOrders orders = Orders();
        await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m));
        await Submit(orders, "pg-2", Limit(id: 2, quantity: 1m, price: 480m));
        await Submit(orders, "pg-3", Limit(id: 3, quantity: 1m, price: 490m));

        _harness.Steam.Count(CreatePath).ShouldBe(1);
        _journal.All.Skip(1).ShouldAllBe(entry => entry.State == SteamOrderJournalState.Waiting && entry.WaitingCode == SteamWaitingCode.SingleBuyOrderSlot);

        await orders.Cancel(Account(), "pg-1", CancellationToken.None);
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _harness.Steam.Count(CreatePath).ShouldBe(2);
        _harness.Steam.LastBuyForm["price_total"].ShouldBe("49000");
        (await Published()).ShouldBe(
        [
            ExternalOrderUpdate.Accept(orderId: 1),
            ExternalOrderUpdate.AcceptLocally(orderId: 2),
            ExternalOrderUpdate.AcceptLocally(orderId: 3),
            ExternalOrderUpdate.ConfirmCancel(orderId: 1),
            ExternalOrderUpdate.Accept(orderId: 3)
        ]);
    }

    [Test]
    public async Task AFreedSlotGoesToTheNearestBuyWhoseBotTheCoreReleases()
    {
        var other = Guid.NewGuid();
        SteamBuyOrders orders = Orders();
        await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m));
        await Submit(orders, "pg-2", Limit(id: 2, quantity: 1m, price: 490m));
        SteamOrderJournalEntry held = (await _journal.Get("pg-2", CancellationToken.None))!;
        _journal.Put(held with { ClientOrderId = "pg-3", BotId = other, EngineOrderId = 3, PriceMinorUnits = 48000 });
        await orders.Cancel(Account(), "pg-1", CancellationToken.None);
        _waiting.Held.Add(Bot);

        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _harness.Steam.Count(CreatePath).ShouldBe(2);
        _harness.Steam.LastBuyForm["price_total"].ShouldBe("48000");
        (await _journal.Get("pg-2", CancellationToken.None))!.State.ShouldBe(SteamOrderJournalState.Waiting);
        (await _journal.Get("pg-3", CancellationToken.None))!.State.ShouldBe(SteamOrderJournalState.Active);
    }

    [Test]
    public async Task AWaitingBuyOfAHeldBotIsNotPlacedUntilTheCoreReleasesIt()
    {
        SteamBuyOrders orders = Orders();
        await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m));
        await Submit(orders, "pg-2", Limit(id: 2, quantity: 1m, price: 490m));
        await orders.Cancel(Account(), "pg-1", CancellationToken.None);
        _waiting.Held.Add(Bot);

        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        _harness.Steam.Count(CreatePath).ShouldBe(1);

        _waiting.Held.Clear();
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _harness.Steam.Count(CreatePath).ShouldBe(2);
        (await _journal.Get("pg-2", CancellationToken.None))!.State.ShouldBe(SteamOrderJournalState.Active);
    }

    [Test]
    public async Task TheCoreHearsWhyABuyWaitsAndWhenItStops()
    {
        SteamBuyOrders orders = Orders();
        await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m));
        await Submit(orders, "pg-2", Limit(id: 2, quantity: 1m, price: 480m));
        await orders.Cancel(Account(), "pg-1", CancellationToken.None);
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _waiting.Reports.ShouldBe(
        [
            (1L, null, null),
            (2L, SteamWaitingCode.SingleBuyOrderSlot, null),
            (2L, null, null)
        ]);
    }

    [Test]
    public async Task AWaitingBuyCancelsLocally()
    {
        SteamBuyOrders orders = Orders();
        await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m));
        await Submit(orders, "pg-2", Limit(id: 2, quantity: 1m, price: 480m));

        await orders.Cancel(Account(), "pg-2", CancellationToken.None);

        _harness.Steam.Count("/market/cancelbuyorder/").ShouldBe(0);
        (await _journal.Get("pg-2", CancellationToken.None))!.Outcome.ShouldBe(SteamOrderOutcome.Cancelled);
    }

    [Test]
    public async Task ARestartAfterThePlaceStartedNeverPostsAgain()
    {
        SteamBuyOrders orders = Orders();

        await Should.ThrowAsync<InvalidOperationException>(() => orders.Submit(
            Account(),
            "pg-1",
            Limit(id: 1, quantity: 1m, price: 500m),
            _ => throw new InvalidOperationException("The process stops here."),
            CancellationToken.None));
        SteamBuyOrders restarted = Orders();

        await Should.ThrowAsync<SteamOrderUnresolvedException>(() => Submit(restarted, "pg-1", Limit(id: 1, quantity: 1m, price: 500m)));
        await restarted.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _harness.Steam.Count(CreatePath).ShouldBe(0);
        _journal.All.Single().State.ShouldBe(SteamOrderJournalState.Placing);
    }

    [Test]
    public async Task ARestartBeforeThePlaceStartedPlacesOnce()
    {
        SteamBuyOrders orders = Orders();
        await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m));
        await Submit(orders, "pg-2", Limit(id: 2, quantity: 1m, price: 480m));
        _journal.Put((await _journal.Get("pg-1", CancellationToken.None))! with { State = SteamOrderJournalState.Done, Outcome = SteamOrderOutcome.Cancelled });
        _harness.Steam.BuyOrders["9001"].Active = false;
        SteamBuyOrders restarted = Orders();

        await Submit(restarted, "pg-2", Limit(id: 2, quantity: 1m, price: 480m));
        await Submit(restarted, "pg-2", Limit(id: 2, quantity: 1m, price: 480m));

        _harness.Steam.Count(CreatePath).ShouldBe(2);
        (await _journal.Get("pg-2", CancellationToken.None))!.State.ShouldBe(SteamOrderJournalState.Active);
    }

    [Test]
    public async Task ARateLimitAfterTheWriteIsUnknownAndNeverRetried()
    {
        SteamBuyOrders orders = Orders();
        _harness.Steam.Override = request => request.RequestUri!.AbsolutePath == CreatePath ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : null;

        await Should.ThrowAsync<SteamOrderUnresolvedException>(() => Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m)));
        _harness.Steam.Override = null;
        await Should.ThrowAsync<SteamOrderUnresolvedException>(() => Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m)));
        await Submit(orders, "pg-2", Limit(id: 2, quantity: 1m, price: 480m));
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _harness.Steam.Count(CreatePath).ShouldBe(1);
        (await _journal.Get("pg-1", CancellationToken.None))!.State.ShouldBe(SteamOrderJournalState.Unknown);
        (await _journal.Get("pg-2", CancellationToken.None))!.WaitingCode.ShouldBe(SteamWaitingCode.SingleBuyOrderSlot);
    }

    [Test]
    public async Task AnUnsentBuyWaitsAndIsPlacedLater()
    {
        SteamBuyOrders orders = Orders();
        await _harness.Store.Open(SteamSessionHarness.Credential, SteamProtectionReason.Captcha, _harness.Clock.GetUtcNow(), CancellationToken.None);

        await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m));
        SteamOrderJournalEntry waiting = (await _journal.Get("pg-1", CancellationToken.None))!;
        await _harness.Store.Clear(SteamSessionHarness.Credential, CancellationToken.None);
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        waiting.State.ShouldBe(SteamOrderJournalState.Waiting);
        waiting.WaitingCode.ShouldBe(SteamWaitingCode.RateLimited);
        waiting.PlaceStartedAt.ShouldBeNull();
        _harness.Steam.Count(CreatePath).ShouldBe(1);
        (await Published()).ShouldBe([ExternalOrderUpdate.AcceptLocally(orderId: 1), ExternalOrderUpdate.Accept(orderId: 1)]);
    }

    [Test]
    public async Task ABuyThatNeedsAConfirmationWaitsLocallyAndCancelsWithoutSteam()
    {
        SteamBuyOrders orders = Orders();
        _harness.Steam.Override = request => request.RequestUri!.AbsolutePath == CreatePath
            ? new HttpResponseMessage(HttpStatusCode.NotAcceptable)
            {
                Content = new StringContent(
                    content: """{"success":22,"need_confirmation":true,"confirmation":{"confirmation_id":"4242"}}""",
                    Encoding.UTF8,
                    "application/json")
            }
            : null;

        await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 500m));
        SteamOrderJournalEntry awaiting = (await _journal.Get("pg-1", CancellationToken.None))!;
        await orders.Cancel(Account(), "pg-1", CancellationToken.None);

        awaiting.State.ShouldBe(SteamOrderJournalState.AwaitingConfirmation);
        awaiting.ConfirmationCreator.ShouldBe("4242");
        awaiting.WaitingCode.ShouldBe(SteamWaitingCode.AwaitingConfirmation);
        _harness.Steam.Count("/market/cancelbuyorder/").ShouldBe(0);
        (await Published()).ShouldBe([ExternalOrderUpdate.AcceptLocally(orderId: 1), ExternalOrderUpdate.ConfirmCancel(orderId: 1)]);
    }

    [Test]
    public async Task AMarketBuyPaysTheLowestListingPlusSlippageAndCancelsItsRemainderAfterTheDeadline()
    {
        _settings = SteamBotSettings.Defaults with { MarketBuySlippage = 0.05m };
        SteamBuyOrders orders = Orders();

        await Submit(orders, "pg-1", Market(id: 1, budget: 1100m));
        _harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        _harness.Steam.Count("/market/cancelbuyorder/").ShouldBe(0);
        _harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await orders.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _harness.Steam.LastBuyForm["quantity"].ShouldBe("2");
        _harness.Steam.LastBuyForm["price_total"].ShouldBe("109200");
        _harness.Steam.Count("/market/cancelbuyorder/").ShouldBe(1);
        (await Published()).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1), ExternalOrderUpdate.ConfirmCancel(orderId: 1)]);
    }

    [Test]
    public async Task ACancelOfAPartlyFilledOrderWaitsForItsHistory()
    {
        SteamBuyOrders orders = Orders();
        await Submit(orders, "pg-1", Limit(id: 1, quantity: 2m, price: 500m));
        _harness.Steam.BuyOrders["9001"].Remaining = 1;

        await Should.ThrowAsync<SteamOrderUnresolvedException>(() => orders.Cancel(Account(), "pg-1", CancellationToken.None));

        (await _journal.Get("pg-1", CancellationToken.None))!.State.ShouldBe(SteamOrderJournalState.Cancelling);
    }

    [Test]
    public async Task AMarketBuyWithoutListingsOrAFractionalPriceIsRejectedUnsent()
    {
        _harness.Steam.LowestPrice = null;
        SteamBuyOrders orders = Orders();

        ExternalOrderUpdate? market = await Submit(orders, "pg-1", Market(id: 1, budget: 1000m));
        ExternalOrderUpdate? fractional = await Submit(orders, "pg-2", Limit(id: 2, quantity: 1m, price: 500.005m));
        ExternalOrderUpdate? partial = await Submit(orders, "pg-3", Limit(id: 3, quantity: 1.5m, price: 500m));

        market!.Reason.ShouldBe(RejectionReason.InstrumentUnavailable);
        fractional!.Reason.ShouldBe(RejectionReason.InvalidPrice);
        partial!.Reason.ShouldBe(RejectionReason.InvalidAmount);
        _journal.All.ShouldBeEmpty();
        _harness.Steam.Count(CreatePath).ShouldBe(0);
    }

    [Test]
    public async Task AWalletOfWholeTengeBuysOnlyAtWholeTenge()
    {
        _fees = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m, increment: 100);
        _settings = SteamBotSettings.Defaults with { MarketBuySlippage = 0.051m };
        SteamBuyOrders orders = Orders();

        ExternalOrderUpdate? tiyn = await Submit(orders, "pg-1", Limit(id: 1, quantity: 1m, price: 512.34m));
        ExternalOrderUpdate? market = await Submit(orders, "pg-2", Market(id: 2, budget: 1100m));

        tiyn!.Reason.ShouldBe(RejectionReason.InvalidPrice);
        market.ShouldBeNull();
        _harness.Steam.Count(CreatePath).ShouldBe(1);

        // 520 ₸ with 5.1% slippage is 546.52 ₸, raised to 547 ₸; 1100 ₸ buys two.
        _harness.Steam.LastBuyForm["quantity"].ShouldBe("2");
        _harness.Steam.LastBuyForm["price_total"].ShouldBe("109400");
    }

    private static SteamOptions Options()
    {
        return new SteamOptions
        {
            Enabled = true,
            CommunityBaseAddress = MockSteam.Community,
            LoginBaseAddress = MockSteam.Login,
            ApiBaseAddress = MockSteam.Api,
            WriteSpacing = TimeSpan.Zero
        };
    }

    private SteamBotAccount Account()
    {
        SteamInstrument.TryParse($"KZT:730:{Item}", out SteamInstrument? instrument).ShouldBeTrue();

        return new SteamBotAccount(Bot, SteamSessionHarness.Owner, SteamSessionHarness.Credential, instrument, _fees, _settings);
    }

    private static OrderSnapshot Limit(long id, decimal quantity, decimal price)
    {
        return new OrderSnapshot(
            id,
            Tag: null,
            OrderSide.Buy,
            OrderType.Limit,
            quantity,
            price,
            QuoteBudget: null,
            quantity * price,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.Active);
    }

    private static OrderSnapshot Market(long id, decimal budget)
    {
        return new OrderSnapshot(
            id,
            Tag: null,
            OrderSide.Buy,
            OrderType.Market,
            Quantity: null,
            LimitPrice: null,
            budget,
            budget,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.Active);
    }

    private SteamBuyOrders Orders()
    {
        IOptions<SteamOptions> options = Microsoft.Extensions.Options.Options.Create(_harness.Options);

        return new SteamBuyOrders(_journal, _harness.Sessions(), new SteamBuyOrderClient(options), _updates, new FakeSteamBotAccounts(Account), _waiting, _harness.Clock);
    }

    private Task<ExternalOrderUpdate?> Submit(SteamBuyOrders orders, string clientOrderId, OrderSnapshot order)
    {
        return orders.Submit(Account(), clientOrderId, order, _ => Task.CompletedTask, CancellationToken.None);
    }

    private async Task<IReadOnlyList<ExternalOrderUpdate>> Published()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var seen = new List<ExternalOrderUpdate>();

        try
        {
            await foreach (ExternalOrderUpdate update in _updates.Read(Bot, stop.Token))
            {
                seen.Add(update);
            }
        }
        catch (OperationCanceledException)
        {
            // Everything published so far has been read.
        }

        return seen;
    }
}