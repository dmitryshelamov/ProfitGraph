using System.Globalization;
using System.Text;

using Microsoft.Extensions.Options;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.History;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Market history receipts and the durable cursor against invented pages (Profit Fox Designer ADR 0087).
/// </summary>
public sealed class SteamMarketHistoryTests
{
    private static readonly SteamCurrency Kzt = SteamCurrency.Find("KZT")!;
    private static readonly SellerFeeSchedule Fees = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);

    private SteamSessionHarness _harness = null!;
    private InMemorySteamMarketHistoryStore _store = null!;

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
                HistoryPageSize = 5,
                HistoryOverlap = 2
            }
        };
        _store = new InMemorySteamMarketHistoryStore();
    }

    [Test]
    public async Task EachEventTypeBecomesAReceipt()
    {
        _harness.Steam.History.AddRange(
        [
            new MockSteamHistoryEvent(Type: 1, "501", Seconds: 100, "1001"),
            new MockSteamHistoryEvent(Type: 2, "502", Seconds: 101, "1002"),
            new MockSteamHistoryEvent(Type: 3, "503", Seconds: 102, "1003", "71", Received: 43478),
            new MockSteamHistoryEvent(Type: 4, "504", Seconds: 103, "2004", "72", "3004", Paid: 43478, Fee: 6522)
        ]);

        IReadOnlyList<SteamStoredReceipt> read = (await Read()).ShouldNotBeNull();

        read.Select(stored => (stored.Position, stored.Receipt.Kind, stored.Receipt.ListingId, stored.Receipt.AssetId, stored.Receipt.BuyerPaysMinorUnits,
                stored.Receipt.SellerReceivesMinorUnits))
            .ShouldBe(
            [
                (0L, SteamMarketReceiptKind.Listed, "501", "1001", 0L, 0L),
                (1L, SteamMarketReceiptKind.Cancelled, "502", "1002", 0L, 0L),
                (2L, SteamMarketReceiptKind.Sold, "503", "1003", (long)(Fees.BuyerPrice(sellerAmount: 434.78m, quotePrecision: 2) / 0.01m), 43478L),
                (3L, SteamMarketReceiptKind.Bought, string.Empty, "3004", 50000L, 43478L)
            ]);
        read[3]
            .Receipt.ShouldSatisfyAllConditions(
                receipt => receipt.Id.ShouldBe("4:504:72:103:0"),
                receipt => receipt.MarketHashName.ShouldBe("Dreams & Nightmares Case"),
                receipt => receipt.At.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(103)));
    }

    [Test]
    public async Task TheFirstReadAnchorsAtTheNewestPageThenReadsOnlyNewEventsWithTheOverlap()
    {
        Listed(count: 12);

        (await Read())!.Select(stored => stored.Position).ShouldBe([7L, 8L, 9L, 10L, 11L]);
        (await Read())!.ShouldBeEmpty();
        Listed(count: 3, from: 12);
        (await Read())!.Select(stored => stored.Position).ShouldBe([12L, 13L, 14L]);

        _store.All.Select(stored => stored.Position).ShouldBe([7L, 8L, 9L, 10L, 11L, 12L, 13L, 14L]);
    }

    [Test]
    public async Task ManyNewEventsAreReadOverSeveralPages()
    {
        Listed(count: 3);
        await Read();
        Listed(count: 9, from: 3);

        (await Read())!.Select(stored => stored.Position).ShouldBe([3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L, 11L]);
    }

    [Test]
    public async Task AnOverlapThatChangedStoresNothing()
    {
        Listed(count: 6);
        await Read();
        _harness.Steam.History[5] = _harness.Steam.History[5] with { Seconds = 999 };
        Listed(count: 1, from: 6);

        (await Read()).ShouldBeNull();
        _store.All.Count.ShouldBe(5);
    }

    [Test]
    public async Task HistoryShorterThanStoredStoresNothing()
    {
        Listed(count: 6);
        await Read();
        _harness.Steam.History.RemoveRange(index: 4, count: 2);

        (await Read()).ShouldBeNull();
    }

    [TestCase(
        arg1:
        """{"success":true,"start":0,"total_count":1,"events":[{"event_type":5,"listingid":"1","time_event":1}],"listings":{"1":{"asset":{"appid":730,"contextid":"2","id":"9"}}},"assets":{"730":{"2":{"9":{"market_hash_name":"X","amount":"1"}}}}}""",
        "EventType")]
    [TestCase(
        arg1:
        """{"success":true,"start":0,"total_count":1,"events":[{"event_type":4,"listingid":"1","purchaseid":"2","time_event":1}],"listings":{"1":{"asset":{"appid":730,"contextid":"2","id":"9"}}},"purchases":{"1_2":{"asset":{"appid":730,"contextid":"2","id":"9","new_id":"10","amount":"1"},"paid_amount":5,"paid_fee":1,"currencyid":"2001","failed":0}},"assets":{"730":{"2":{"9":{"market_hash_name":"X","amount":"1"}}}}}""",
        "Currency")]
    [TestCase(
        arg1:
        """{"success":true,"start":0,"total_count":1,"events":[{"event_type":4,"listingid":"1","purchaseid":"2","time_event":1}],"listings":{"1":{"asset":{"appid":730,"contextid":"2","id":"9"}}},"purchases":{"1_2":{"asset":{"appid":730,"contextid":"2","id":"9","new_id":"10"},"paid_amount":5,"paid_fee":1,"currencyid":"2037","needs_rollback":1}},"assets":{"730":{"2":{"9":{"market_hash_name":"X","amount":"1"}}}}}""",
        "Unsettled")]
    [TestCase(
        arg1: """{"success":true,"start":0,"total_count":1,"events":[{"event_type":1,"listingid":"1","time_event":1}],"listings":[],"assets":[]}""",
        "Reference:Listing@1")]
    public void AnythingUnexplainedLeavesThePageUnknown(string json, string reason)
    {
        SteamMarketHistoryParser.Page(Encoding.UTF8.GetBytes(json), Kzt, Fees, out string why).ShouldBeNull();
        why.ShouldBe(reason);
    }

    private void Listed(int count, int from = 0)
    {
        _harness.Steam.History.AddRange(
            Enumerable.Range(from, count)
                .Select(index => new MockSteamHistoryEvent(
                    Type: 1,
                    (500 + index).ToString(CultureInfo.InvariantCulture),
                    1000 + index,
                    (1000 + index).ToString(CultureInfo.InvariantCulture))));
    }

    private Task<IReadOnlyList<SteamStoredReceipt>?> Read()
    {
        var history = new SteamMarketHistory(_store, Options.Create(_harness.Options));

        return history.Read(
            _harness.Sessions().For(SteamSessionHarness.Owner, SteamSessionHarness.Credential),
            SteamSessionHarness.Credential,
            Kzt,
            Fees,
            CancellationToken.None);
    }
}