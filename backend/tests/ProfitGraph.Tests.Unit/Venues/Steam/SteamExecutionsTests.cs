using System.Net;
using System.Text;

using Microsoft.Extensions.Options;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Confirmations;
using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Inventory;
using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Fills from market history against the mock: purchases by the buy's own counters, sales by their listings, and the
///     bot's items from what it bought (ADR 0022; Profit Fox Designer ADR 0087).
/// </summary>
public sealed class SteamExecutionsTests
{
    private const string Item = "Dreams & Nightmares Case";

    private static readonly Guid Bot = Guid.NewGuid();
    private static readonly SteamCurrency Kzt = SteamCurrency.Find("KZT")!;
    private static readonly SellerFeeSchedule Fees = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);

    private SteamSessionHarness _harness = null!;
    private InMemorySteamOrderJournal _journal = null!;
    private InMemorySteamMarketHistoryStore _receipts = null!;
    private InMemorySteamMarketAttributions _attributions = null!;
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
        _receipts = new InMemorySteamMarketHistoryStore();
        _attributions = new InMemorySteamMarketAttributions();
        _updates = new SteamOrderUpdates();
        _waiting = new RecordingSteamOrderWaiting();
        _owned = new FakeSteamOwnedAssets();
    }

    [Test]
    public async Task ABuyFillsWhenItsCountersMatchThePurchases()
    {
        await Buy(quantity: 2);
        _harness.Steam.BuyOrders["9001"].Remaining = 0;
        _harness.Steam.BuyOrders["9001"].Active = false;
        Bought("701", "3001", paid: 43478, fee: 6522);
        Bought("702", "3002", paid: 43000, fee: 6450);

        (await Advance()).ShouldBeTrue();

        (await Entry("pg-1")).Outcome.ShouldBe(SteamOrderOutcome.Filled);
        (await Published()).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1), ExternalOrderUpdate.Final(orderId: 1, grossBase: 2m, grossQuote: 994.50m, fee: 0m)]);
    }

    [Test]
    public async Task APartlyFilledBuyIsCancelledForItsRemainder()
    {
        (SteamBuyOrders buys, _) = await Buy(quantity: 2);
        _harness.Steam.BuyOrders["9001"].Remaining = 1;
        Bought("701", "3001", paid: 43478, fee: 6522);

        await Advance();
        await Should.ThrowAsync<SteamOrderUnresolvedException>(() => buys.Cancel(Account(), "pg-1", CancellationToken.None));
        await Advance();

        (await Entry("pg-1")).Outcome.ShouldBe(SteamOrderOutcome.Cancelled);
        (await Published()).ShouldBe(
        [
            ExternalOrderUpdate.Accept(orderId: 1),
            ExternalOrderUpdate.Partial(orderId: 1, grossBase: 1m, grossQuote: 500m, fee: 0m),
            ExternalOrderUpdate.ConfirmCancel(orderId: 1)
        ]);
    }

    [Test]
    public async Task AnOwnersOwnPurchaseOfTheItemAttributesNothing()
    {
        await Buy(quantity: 2);
        _harness.Steam.BuyOrders["9001"].Remaining = 1;
        Bought("701", "3001", paid: 43478, fee: 6522);
        Bought("702", "3002", paid: 43478, fee: 6522);

        await Advance();

        _attributions.All.ShouldBeEmpty();
        (await Entry("pg-1")).State.ShouldBe(SteamOrderJournalState.Active);
    }

    [Test]
    public async Task APurchaseAboveTheBuysPriceIsNotItsOwn()
    {
        await Buy(quantity: 1);
        _harness.Steam.BuyOrders["9001"].Remaining = 0;
        Bought("701", "3001", paid: 52174, fee: 7826);

        await Advance();

        _attributions.All.ShouldBeEmpty();
    }

    [Test]
    public async Task ASaleFillsTheSellByItsListing()
    {
        _owned.Assets.Add(new SteamOwnedAsset(AppId: 730, "2", "1001", DateTimeOffset.UnixEpoch));
        IOptions<SteamOptions> options = Options.Create(_harness.Options);
        var settlement = new SteamSellSettlement(_journal, _updates, _waiting, _harness.Clock);
        var sells = new SteamSells(_journal, _owned, _harness.Sessions(), new SteamListingClient(options), settlement, _updates, _waiting, _harness.Clock);
        var confirmations = new SteamConfirmations(
            _journal,
            _harness.Sessions(),
            new SteamConfirmationClient(_harness.Scopes()),
            new SteamBuyOrders(_journal, _harness.Sessions(), new SteamBuyOrderClient(options), _updates, new FakeSteamBotAccounts(Account), _waiting, _harness.Clock),
            settlement,
            _waiting,
            _harness.Clock);
        var order = new OrderSnapshot(
            Id: 1,
            Tag: null,
            OrderSide.Sell,
            OrderType.Limit,
            Quantity: 1m,
            LimitPrice: 100m,
            QuoteBudget: null,
            Reservation: 1m,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.Active);
        await sells.Submit(Account(), "pg-1", order, _ => Task.CompletedTask, CancellationToken.None);
        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        await confirmations.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        SteamAssetReservation listed = (await _journal.Reservations("pg-1", CancellationToken.None)).Single();
        long seller = _harness.Steam.Listings.Single().SellerPrice;
        _harness.Steam.History.Add(new MockSteamHistoryEvent(Type: 3, listed.ListingId!, Now(), "1001", "801", Received: seller));

        await Advance();

        decimal buyer = Fees.BuyerPrice(seller * 0.01m, quotePrecision: 2);
        (await _journal.Reservations("pg-1", CancellationToken.None)).Single().State.ShouldBe(SteamReservationState.Sold);
        (await Entry("pg-1")).Outcome.ShouldBe(SteamOrderOutcome.Filled);
        (await Published()).ShouldContain(ExternalOrderUpdate.Final(orderId: 1, grossBase: 1m, buyer, buyer - seller * 0.01m));
    }

    [Test]
    public async Task ARemovedListingIsReleasedAndItsCancelConfirmedOnceHistoryShowsTheItemBack()
    {
        _owned.Assets.Add(new SteamOwnedAsset(AppId: 730, "2", "1001", DateTimeOffset.UnixEpoch));
        IOptions<SteamOptions> options = Options.Create(_harness.Options);
        var settlement = new SteamSellSettlement(_journal, _updates, _waiting, _harness.Clock);
        var sells = new SteamSells(_journal, _owned, _harness.Sessions(), new SteamListingClient(options), settlement, _updates, _waiting, _harness.Clock);
        var confirmations = new SteamConfirmations(
            _journal,
            _harness.Sessions(),
            new SteamConfirmationClient(_harness.Scopes()),
            new SteamBuyOrders(_journal, _harness.Sessions(), new SteamBuyOrderClient(options), _updates, new FakeSteamBotAccounts(Account), _waiting, _harness.Clock),
            settlement,
            _waiting,
            _harness.Clock);
        var order = new OrderSnapshot(
            Id: 1,
            Tag: null,
            OrderSide.Sell,
            OrderType.Limit,
            Quantity: 1m,
            LimitPrice: 100m,
            QuoteBudget: null,
            Reservation: 1m,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.Active);
        await sells.Submit(Account(), "pg-1", order, _ => Task.CompletedTask, CancellationToken.None);
        await sells.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        await confirmations.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
        await sells.Cancel(Account(), "pg-1", CancellationToken.None);
        SteamAssetReservation removed = (await _journal.Reservations("pg-1", CancellationToken.None)).Single();
        removed.State.ShouldBe(SteamReservationState.Removed);

        _harness.Steam.History.Add(new MockSteamHistoryEvent(Type: 2, removed.ListingId!, Now(), "1001"));
        await Advance();

        (await _journal.Reservations("pg-1", CancellationToken.None)).Single().State.ShouldBe(SteamReservationState.Released);
        (await Entry("pg-1")).Outcome.ShouldBe(SteamOrderOutcome.Cancelled);
        (await _attributions.Of(SteamSessionHarness.Credential, CancellationToken.None)).ShouldHaveSingleItem().ClientOrderId.ShouldBe("pg-1");
        (await Published()).ShouldContain(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
    }

    [Test]
    public async Task TheBotOwnsWhatItBoughtWhileItIsInTheInventory()
    {
        await Buy(quantity: 2);
        _harness.Steam.BuyOrders["9001"].Remaining = 0;
        Bought("701", "3001", paid: 43478, fee: 6522);
        Bought("702", "3002", paid: 43478, fee: 6522);
        await Advance();
        Inventory("3001", "3002", "9999");

        IReadOnlyList<SteamOwnedAsset> owned = await new SteamOwnedAssetsFromHistory(
                _receipts,
                _attributions,
                new SteamInventoryReader(Options.Create(_harness.Options), _harness.Clock),
                _harness.Sessions(),
                _harness.Clock)
            .Owned(Account(), CancellationToken.None);

        owned.Select(asset => asset.AssetId).ShouldBe(["3001", "3002"], ignoreOrder: true);
        owned.Single(asset => asset.AssetId == "3002").UnlockAt.ShouldBe(new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 7, minute: 0, second: 0, TimeSpan.Zero));
    }

    private static SteamBotAccount Account()
    {
        SteamInstrument.TryParse($"KZT:730:{Item}", out SteamInstrument? instrument).ShouldBeTrue();

        return new SteamBotAccount(Bot, SteamSessionHarness.Owner, SteamSessionHarness.Credential, instrument, Fees);
    }

    private long Now()
    {
        return _harness.Clock.GetUtcNow().ToUnixTimeSeconds();
    }

    private void Bought(string listingId, string newAssetId, long paid, long fee)
    {
        _harness.Steam.History.Add(new MockSteamHistoryEvent(Type: 4, listingId, Now(), "20" + listingId, "9" + listingId, newAssetId, paid, fee));
    }

    private void Inventory(params string[] assetIds)
    {
        _harness.Steam.Override = request =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path == $"/profiles/{MockSteam.SteamId}/inventory/")
            {
                return Answer(body: """<script>var g_rgAppContextData = {"730":{"rgContexts":{"2":{}}}};</script>""", "text/html");
            }

            if (path != $"/inventory/{MockSteam.SteamId}/730/2")
            {
                return null;
            }

            string assets = string.Join(
                separator: ',',
                assetIds.Select((id, index) =>
                    $$"""{"appid":730,"contextid":"2","assetid":"{{id}}","classid":"{{(index == 1 ? 11 : 10)}}","instanceid":"0","amount":"1"}"""));

            return Answer(
                $$$"""
                   {"success":1,"total_inventory_count":{{{assetIds.Length}}},"assets":[{{{assets}}}],
                    "descriptions":[{"appid":730,"classid":"10","instanceid":"0","market_hash_name":"{{{Item}}}","marketable":1,"tradable":1},
                                    {"appid":730,"classid":"11","instanceid":"0","market_hash_name":"{{{Item}}}","marketable":0,"tradable":0,
                                     "owner_descriptions":[{"type":"html","value":"Tradable/Marketable After 1 Nov @ 7:00am"}]}]}
                   """);
        };
    }

    private static HttpResponseMessage Answer(string body, string mediaType = "application/json")
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
    }

    private async Task<(SteamBuyOrders Buys, SteamOrderJournalEntry Entry)> Buy(int quantity)
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);
        var buys = new SteamBuyOrders(
            _journal,
            _harness.Sessions(),
            new SteamBuyOrderClient(options),
            _updates,
            new FakeSteamBotAccounts(Account),
            _waiting,
            _harness.Clock);
        var order = new OrderSnapshot(
            Id: 1,
            Tag: null,
            OrderSide.Buy,
            OrderType.Limit,
            quantity,
            LimitPrice: 500m,
            QuoteBudget: null,
            quantity * 500m,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.Active);
        (await buys.Submit(Account(), "pg-1", order, _ => Task.CompletedTask, CancellationToken.None)).ShouldBeNull();
        _harness.Clock.Advance(TimeSpan.FromSeconds(30));

        return (buys, await Entry("pg-1"));
    }

    private Task<bool> Advance()
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);
        var executions = new SteamExecutions(
            _journal,
            _receipts,
            _attributions,
            new SteamMarketHistory(_receipts, options),
            _harness.Sessions(),
            new SteamBuyOrderClient(options),
            _updates,
            options,
            _harness.Clock);

        return executions.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, Kzt, Fees, CancellationToken.None);
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