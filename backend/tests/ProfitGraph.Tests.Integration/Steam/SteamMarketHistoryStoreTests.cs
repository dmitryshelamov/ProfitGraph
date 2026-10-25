using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Persistence.Repositories;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     Market history receipts in PostgreSQL: written once at their positions, and appended only to the history a reader
///     started from (ADR 0022; Profit Fox Designer ADR 0087).
/// </summary>
public sealed class SteamMarketHistoryStoreTests
{
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
    public async Task ReceiptsAppendOnlyToTheHistoryTheReaderSaw()
    {
        Guid credential = (await BotSeed.NewBot()).CredentialId.Value;
        var store = new SteamMarketHistoryRepository(_dataSource);
        SteamStoredReceipt[] anchor = [Stored(position: 7, "1:501:0:100:0"), Stored(position: 8, "4:502:9:101:0")];

        (await store.Next(credential, CancellationToken.None)).ShouldBeNull();
        (await store.Append(credential, expectedNext: null, anchor, CancellationToken.None)).ShouldBeTrue();
        (await store.Append(credential, expectedNext: null, [Stored(position: 9, "1:503:0:102:0")], CancellationToken.None)).ShouldBeFalse();
        (await store.Append(credential, expectedNext: 9, [Stored(position: 9, "1:503:0:102:0")], CancellationToken.None)).ShouldBeTrue();

        (await store.Next(credential, CancellationToken.None)).ShouldBe(10);
        (await store.From(credential, position: 8, CancellationToken.None)).ShouldBe([anchor[1], Stored(position: 9, "1:503:0:102:0")]);
    }

    [Test]
    public async Task AReceiptIsAttributedToOneOrderOnce()
    {
        Bot bot = await BotSeed.NewBot();

        using (IServiceScope scope = _host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
                .ExecuteAsync(
                    async () =>
                    {
                        await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                        return true;
                    },
                    CancellationToken.None);
        }

        Guid credential = bot.CredentialId.Value;
        string clientOrderId = "pg-h-" + Guid.NewGuid().ToString("N")[..8];
        var journal = new SteamOrderJournalRepository(_dataSource);
        await journal.Add(
            new SteamOrderJournalEntry(
                clientOrderId,
                bot.Id.Value,
                credential,
                EngineOrderId: 1,
                "KZT:730:Dreams & Nightmares Case",
                OrderSide.Buy,
                OrderType.Limit,
                Quantity: 1,
                PriceMinorUnits: 50000,
                SteamOrderJournalState.Waiting,
                WaitingCode: null,
                SteamOrderId: null,
                ConfirmationCreator: null,
                Outcome: null,
                PlaceStartedAt: null,
                Deadline: null,
                BotSeed.Now,
                BotSeed.Now),
            CancellationToken.None);
        await new SteamMarketHistoryRepository(_dataSource).Append(credential, expectedNext: null, [Stored(position: 0, "4:502:9:101:0")], CancellationToken.None);
        var attributions = new SteamMarketAttributionRepository(_dataSource);
        var attribution = new SteamAttribution("4:502:9:101:0", clientOrderId, bot.Id.Value);

        (await attributions.Attribute(credential, [attribution], BotSeed.Now, CancellationToken.None)).ShouldBeTrue();
        (await attributions.Attribute(credential, [attribution with { ClientOrderId = clientOrderId }], BotSeed.Now, CancellationToken.None)).ShouldBeFalse();
        (await attributions.Of(credential, CancellationToken.None)).ShouldBe([attribution]);
    }

    private static SteamStoredReceipt Stored(long position, string id)
    {
        return new SteamStoredReceipt(
            position,
            new SteamMarketReceipt(
                id,
                id.StartsWith('4') ? SteamMarketReceiptKind.Bought : SteamMarketReceiptKind.Listed,
                id.StartsWith('4') ? string.Empty : id.Split(':')[1],
                AppId: 730,
                "Dreams & Nightmares Case",
                "2",
                "100" + position,
                Quantity: 1,
                id.StartsWith('4') ? 50000 : 0,
                id.StartsWith('4') ? 43478 : 0,
                DateTimeOffset.FromUnixTimeSeconds(100 + position)));
    }
}