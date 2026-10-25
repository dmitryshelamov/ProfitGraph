using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Persistence.Repositories;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     The Steam order journal in PostgreSQL: one buy that Steam may have per item and account, and changes that apply
///     only to the state they expect (ADR 0022).
/// </summary>
public sealed class SteamOrderJournalTests
{
    private const string Item = "KZT:730:Dreams & Nightmares Case";

    private ProfitGraphApiFactory _api = null!;
    private IHost _host = null!;
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
        _host = BotTestHost.Create();
        _dataSource = NpgsqlDataSource.Create(TestDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _dataSource.DisposeAsync();
        _host.Dispose();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task OneBuyOfAnItemHoldsTheSlotUntilItIsDone()
    {
        Bot bot = await AddBot();
        var journal = new SteamOrderJournalRepository(_dataSource);
        SteamOrderJournalEntry first = Entry(bot, "pg-a-" + Guid.NewGuid().ToString("N")[..8], price: 50000);
        SteamOrderJournalEntry second = Entry(bot, "pg-b-" + Guid.NewGuid().ToString("N")[..8], price: 49000);
        (await journal.Add(first, CancellationToken.None)).ShouldBeTrue();
        (await journal.Add(second, CancellationToken.None)).ShouldBeTrue();
        (await journal.Add(first, CancellationToken.None)).ShouldBeFalse();

        (await journal.Transition(first with { State = SteamOrderJournalState.Placing }, SteamOrderJournalState.Waiting, CancellationToken.None)).ShouldBeTrue();
        (await journal.Transition(second with { State = SteamOrderJournalState.Placing }, SteamOrderJournalState.Waiting, CancellationToken.None)).ShouldBeFalse();
        (await journal.Transition(first with { State = SteamOrderJournalState.Active }, SteamOrderJournalState.Waiting, CancellationToken.None)).ShouldBeFalse();

        SteamOrderJournalEntry active = first with { State = SteamOrderJournalState.Active, SteamOrderId = "9001", UpdatedAt = BotSeed.Now.AddSeconds(1) };
        (await journal.Transition(active, SteamOrderJournalState.Placing, CancellationToken.None)).ShouldBeTrue();
        (await journal.Get(first.ClientOrderId, CancellationToken.None)).ShouldBe(active);

        SteamOrderJournalEntry done = active with { State = SteamOrderJournalState.Done, Outcome = SteamOrderOutcome.Cancelled };
        (await journal.Transition(done, SteamOrderJournalState.Active, CancellationToken.None)).ShouldBeTrue();
        (await journal.Transition(second with { State = SteamOrderJournalState.Placing }, SteamOrderJournalState.Waiting, CancellationToken.None)).ShouldBeTrue();

        (await journal.Open(bot.CredentialId.Value, CancellationToken.None)).Select(entry => entry.ClientOrderId).ShouldBe([second.ClientOrderId]);
        (await journal.OpenAccounts(CancellationToken.None)).ShouldContain((bot.CredentialId.Value, bot.Id.Value));
    }

    [Test]
    public async Task AnApprovalHoldsTheSlot()
    {
        Bot bot = await AddBot();
        var journal = new SteamOrderJournalRepository(_dataSource);
        SteamOrderJournalEntry first = Entry(bot, "pg-c-" + Guid.NewGuid().ToString("N")[..8], price: 50000);
        SteamOrderJournalEntry second = Entry(bot, "pg-d-" + Guid.NewGuid().ToString("N")[..8], price: 49000);
        await journal.Add(first, CancellationToken.None);
        await journal.Add(second, CancellationToken.None);

        (await journal.Transition(first with { State = SteamOrderJournalState.Approving }, SteamOrderJournalState.Waiting, CancellationToken.None)).ShouldBeTrue();
        (await journal.Transition(first with { State = SteamOrderJournalState.Approved }, SteamOrderJournalState.Approving, CancellationToken.None)).ShouldBeTrue();
        (await journal.Transition(second with { State = SteamOrderJournalState.Placing }, SteamOrderJournalState.Waiting, CancellationToken.None)).ShouldBeFalse();
        (await journal.Get(first.ClientOrderId, CancellationToken.None))!.State.ShouldBe(SteamOrderJournalState.Approved);
    }

    [Test]
    public async Task AnAssetIsReservedByOneOpenSellOnly()
    {
        Bot bot = await AddBot();
        var journal = new SteamOrderJournalRepository(_dataSource);
        SteamOrderJournalEntry first = Entry(bot, "pg-e-" + Guid.NewGuid().ToString("N")[..8], price: 50000) with { Side = OrderSide.Sell };
        SteamOrderJournalEntry second = Entry(bot, "pg-f-" + Guid.NewGuid().ToString("N")[..8], price: 50000) with { Side = OrderSide.Sell };
        SteamAssetReservation asset = Reservation(first, "1001");

        (await journal.AddSell(first, [asset, Reservation(first, "1002")], CancellationToken.None)).ShouldBeTrue();
        (await journal.AddSell(second, [Reservation(second, "1003"), Reservation(second, "1001")], CancellationToken.None)).ShouldBeFalse();
        (await journal.Get(second.ClientOrderId, CancellationToken.None)).ShouldBeNull();

        SteamAssetReservation released = asset with { State = SteamReservationState.Released };
        (await journal.Transition(released, SteamReservationState.Reserved, CancellationToken.None)).ShouldBeTrue();
        (await journal.AddSell(second, [Reservation(second, "1001")], CancellationToken.None)).ShouldBeTrue();
        (await journal.Reserved(bot.CredentialId.Value, CancellationToken.None)).Select(item => (item.ClientOrderId, item.AssetId))
            .ShouldBe([(first.ClientOrderId, "1002"), (second.ClientOrderId, "1001")], ignoreOrder: true);
        (await journal.Reservations(first.ClientOrderId, CancellationToken.None)).Single(item => item.AssetId == "1001").ShouldBe(released);
    }

    private static SteamAssetReservation Reservation(SteamOrderJournalEntry entry, string assetId)
    {
        return new SteamAssetReservation(
            entry.ClientOrderId,
            entry.CredentialId,
            AppId: 730,
            "2",
            assetId,
            BotSeed.Now,
            SellerMinorUnits: 43478,
            SteamReservationState.Reserved,
            ListingId: null,
            BotSeed.Now,
            BotSeed.Now);
    }

    private static SteamOrderJournalEntry Entry(Bot bot, string clientOrderId, long price)
    {
        return new SteamOrderJournalEntry(
            clientOrderId,
            bot.Id.Value,
            bot.CredentialId.Value,
            EngineOrderId: 1,
            Item,
            OrderSide.Buy,
            OrderType.Limit,
            Quantity: 1,
            price,
            SteamOrderJournalState.Waiting,
            WaitingCode: null,
            SteamOrderId: null,
            ConfirmationCreator: null,
            Outcome: null,
            PlaceStartedAt: null,
            Deadline: null,
            BotSeed.Now,
            BotSeed.Now);
    }

    private async Task<Bot> AddBot()
    {
        Bot bot = await BotSeed.NewBot();
        using IServiceScope scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return bot;
    }
}