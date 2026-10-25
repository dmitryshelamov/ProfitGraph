using System.Net;

using Microsoft.Extensions.Options;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Confirmations;
using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Steam sells against the mock: one listing per owned, unlocked asset, each asset reserved once, listings confirmed
///     on the phone and removed on cancel (ADR 0022).
/// </summary>
public sealed class SteamSellsTests
{
    private const string Item = "Dreams & Nightmares Case";
    private const string SellPath = "/market/sellitem/";

    private static readonly Guid Bot = Guid.NewGuid();
    private static readonly SellerFeeSchedule Fees = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);

    private SteamSessionHarness _harness = null!;
    private InMemorySteamOrderJournal _journal = null!;
    private SteamOrderUpdates _updates = null!;
    private RecordingSteamOrderWaiting _waiting = null!;
    private FakeSteamOwnedAssets _owned = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SteamSessionHarness
        {
            Options = new SteamOptions
            {
                Enabled = true,
                CommunityBaseAddress = MockSteam.Community,
                LoginBaseAddress = MockSteam.Login,
                ApiBaseAddress = MockSteam.Api,
                WriteSpacing = TimeSpan.Zero
            }
        };
        _harness.Clock.SetUtcNow(_harness.Steam.ServerTime);
        _journal = new InMemorySteamOrderJournal();
        _updates = new SteamOrderUpdates();
        _waiting = new RecordingSteamOrderWaiting();
        _owned = new FakeSteamOwnedAssets();
    }

    [Test]
    public async Task EachUnlockedAssetIsListedAtWhatTheSellerReceivesThenConfirmed()
    {
        _owned.Assets.AddRange([Asset("1001"), Asset("1002"), Asset("1003", locked: true)]);
        (SteamSells sells, SteamConfirmations confirmations) = Create();

        (await Submit(sells, "pg-1", Limit(quantity: 2m, price: 100m))).ShouldBeNull();
        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        await confirmations.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        var seller = (long)(Fees.SellerAmount(buyerPrice: 100m, quotePrecision: 2) / 0.01m);
        _harness.Steam.Listings.Select(listing => (listing.AssetId, listing.SellerPrice, listing.Confirmed)).ShouldBe([("1001", seller, true), ("1002", seller, true)]);
        (await _journal.Reservations("pg-1", CancellationToken.None)).ShouldAllBe(reservation =>
            reservation.State == SteamReservationState.Listed && reservation.ListingId != null);
        (await Entry("pg-1")).State.ShouldBe(SteamOrderJournalState.Active);
        (await Published()).ShouldBe([ExternalOrderUpdate.AcceptLocally(orderId: 1), ExternalOrderUpdate.Accept(orderId: 1)]);
    }

    [Test]
    public async Task ItemsUnderTheirHoldAreListedOnceTheyUnlock()
    {
        _owned.Assets.Add(Asset("1001", locked: true));
        (SteamSells sells, _) = Create();

        await Submit(sells, "pg-1", Limit(quantity: 1m, price: 100m));
        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        _harness.Steam.Count(SellPath).ShouldBe(0);
        (await Entry("pg-1")).WaitingCode.ShouldBe(SteamWaitingCode.SettlementHold);

        _harness.Clock.Advance(TimeSpan.FromDays(1));
        _harness.Steam.ServerTime = _harness.Clock.GetUtcNow();
        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _harness.Steam.Count(SellPath).ShouldBe(1);
        (await Entry("pg-1")).WaitingCode.ShouldBe(SteamWaitingCode.AwaitingConfirmation);
        _waiting.Reports.ShouldBe(
        [
            (1L, SteamWaitingCode.SettlementHold, Asset("1001", locked: true).UnlockAt),
            (1L, SteamWaitingCode.AwaitingConfirmation, null)
        ]);
    }

    [Test]
    public async Task AHeldBotsItemsAreListedAndApprovedOnlyWhileTheCoreReleasesIt()
    {
        _owned.Assets.Add(Asset("1001", locked: true));
        (SteamSells sells, SteamConfirmations confirmations) = Create();
        await Submit(sells, "pg-1", Limit(quantity: 1m, price: 100m));
        _harness.Clock.Advance(TimeSpan.FromDays(1));
        _harness.Steam.ServerTime = _harness.Clock.GetUtcNow();
        _waiting.Held.Add(Bot);

        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        _harness.Steam.Count(SellPath).ShouldBe(0);
        (await _journal.Reservations("pg-1", CancellationToken.None)).Single().State.ShouldBe(SteamReservationState.Reserved);

        _waiting.Held.Clear();
        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        _waiting.Held.Add(Bot);
        await confirmations.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        _harness.Steam.Count(SellPath).ShouldBe(1);
        _harness.Steam.Count("/mobileconf/ajaxop").ShouldBe(0);
        (await _journal.Reservations("pg-1", CancellationToken.None)).Single().State.ShouldBe(SteamReservationState.AwaitingConfirmation);
    }

    [Test]
    public async Task TwoSellsNeverReserveOneItem()
    {
        _owned.Assets.Add(Asset("1001"));
        (SteamSells sells, _) = Create();

        ExternalOrderUpdate? first = await Submit(sells, "pg-1", Limit(quantity: 1m, price: 100m));
        ExternalOrderUpdate? second = await Submit(sells, "pg-2", Limit(quantity: 1m, price: 100m, id: 2));

        first.ShouldBeNull();
        second!.Reason.ShouldBe(RejectionReason.InsufficientBase);
        _harness.Steam.Count(SellPath).ShouldBe(1);
    }

    [Test]
    public async Task AMarketSellListsAtTheHighestBuyOrder()
    {
        _owned.Assets.Add(Asset("1001"));
        (SteamSells sells, _) = Create();

        await Submit(
            sells,
            "pg-1",
            new OrderSnapshot(
                Id: 1,
                Tag: null,
                OrderSide.Sell,
                OrderType.Market,
                Quantity: 1m,
                LimitPrice: null,
                QuoteBudget: null,
                Reservation: 1m,
                "node",
                EligibleFromCandleIndex: 0,
                OrderStatus.Active));

        _harness.Steam.Listings.Single().SellerPrice.ShouldBe((long)(Fees.SellerAmount(buyerPrice: 512.34m, quotePrecision: 2) / 0.01m));
        (await Entry("pg-1")).PriceMinorUnits.ShouldBe(51234);
    }

    [Test]
    public async Task ACancelRemovesTheListingsAndFreesTheItems()
    {
        _owned.Assets.AddRange([Asset("1001"), Asset("1002", locked: true)]);
        (SteamSells sells, SteamConfirmations confirmations) = Create();
        await Submit(sells, "pg-1", Limit(quantity: 2m, price: 100m));
        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        await confirmations.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);

        await sells.Cancel(Account(), "pg-1", CancellationToken.None);
        ExternalOrderUpdate? again = await Submit(sells, "pg-2", Limit(quantity: 2m, price: 100m, id: 2));

        _harness.Steam.Count("/market/removelisting/" + (await _journal.Reservations("pg-1", CancellationToken.None))[0].ListingId).ShouldBe(1);
        (await _journal.Reservations("pg-1", CancellationToken.None)).ShouldAllBe(reservation => reservation.State == SteamReservationState.Released);
        (await Entry("pg-1")).Outcome.ShouldBe(SteamOrderOutcome.Cancelled);
        again.ShouldBeNull();
        (await Published()).ShouldContain(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
    }

    [Test]
    public async Task AnUnclearListingIsUnknownAndNeverSentAgain()
    {
        _owned.Assets.Add(Asset("1001"));
        (SteamSells sells, _) = Create();
        _harness.Steam.Override = request => request.RequestUri!.AbsolutePath == SellPath ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : null;

        await Submit(sells, "pg-1", Limit(quantity: 1m, price: 100m));
        _harness.Steam.Override = null;
        await Submit(sells, "pg-1", Limit(quantity: 1m, price: 100m));

        _harness.Steam.Count(SellPath).ShouldBe(1);
        (await _journal.Reservations("pg-1", CancellationToken.None)).Single().State.ShouldBe(SteamReservationState.Unknown);
        await Should.ThrowAsync<SteamOrderUnresolvedException>(() => sells.Cancel(Account(), "pg-1", CancellationToken.None));
    }

    [Test]
    public async Task ARestartDuringAListingApprovalNeverApprovesAgain()
    {
        _owned.Assets.Add(Asset("1001"));
        (SteamSells sells, SteamConfirmations confirmations) = Create();
        await Submit(sells, "pg-1", Limit(quantity: 1m, price: 100m));
        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        SteamAssetReservation bound = (await _journal.Reservations("pg-1", CancellationToken.None)).Single();
        (await _journal.Transition(bound with { State = SteamReservationState.Approving }, SteamReservationState.AwaitingConfirmation, CancellationToken.None))
            .ShouldBeTrue();

        (await confirmations.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None)).ShouldBeFalse();

        _harness.Steam.Count("/mobileconf/ajaxop").ShouldBe(0);
    }

    [Test]
    public async Task OnlyWholeItemsAreListed()
    {
        _owned.Assets.Add(Asset("1001"));
        (SteamSells sells, _) = Create();

        (await Submit(sells, "pg-1", Limit(quantity: 0.5m, price: 100m)))!.Reason.ShouldBe(RejectionReason.InvalidAmount);
        _journal.All.ShouldBeEmpty();
    }

    private static SteamOwnedAsset Asset(string id, bool locked = false)
    {
        return new SteamOwnedAsset(
            AppId: 730,
            "2",
            id,
            locked ? new DateTimeOffset(year: 2026, month: 10, day: 26, hour: 0, minute: 0, second: 0, TimeSpan.Zero) : DateTimeOffset.UnixEpoch);
    }

    private static SteamBotAccount Account()
    {
        SteamInstrument.TryParse($"KZT:730:{Item}", out SteamInstrument? instrument).ShouldBeTrue();

        return new SteamBotAccount(Bot, SteamSessionHarness.Owner, SteamSessionHarness.Credential, instrument, Fees);
    }

    private static OrderSnapshot Limit(decimal quantity, decimal price, long id = 1)
    {
        return new OrderSnapshot(
            id,
            Tag: null,
            OrderSide.Sell,
            OrderType.Limit,
            quantity,
            price,
            QuoteBudget: null,
            quantity,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.Active);
    }

    private (SteamSells Sells, SteamConfirmations Confirmations) Create()
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);
        var settlement = new SteamSellSettlement(_journal, _updates, _waiting, _harness.Clock);
        var sells = new SteamSells(_journal, _owned, _harness.Sessions(), new SteamListingClient(options), settlement, _updates, _waiting, _harness.Clock);
        var buys = new SteamBuyOrders(
            _journal,
            _harness.Sessions(),
            new SteamBuyOrderClient(options),
            _updates,
            new FakeSteamBotAccounts(Account),
            _waiting,
            _harness.Clock);

        return (sells, new SteamConfirmations(_journal, _harness.Sessions(), new SteamConfirmationClient(_harness.Scopes()), buys, settlement, _waiting, _harness.Clock));
    }

    private Task<ExternalOrderUpdate?> Submit(SteamSells sells, string clientOrderId, OrderSnapshot order)
    {
        return sells.Submit(Account(), clientOrderId, order, _ => Task.CompletedTask, CancellationToken.None);
    }

    private async Task<SteamOrderJournalEntry> Entry(string clientOrderId)
    {
        return (await _journal.Get(clientOrderId, CancellationToken.None)).ShouldNotBeNull();
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