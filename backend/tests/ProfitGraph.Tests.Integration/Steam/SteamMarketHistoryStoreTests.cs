using Npgsql;

using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Persistence.Repositories;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     Market history receipts in PostgreSQL: written once at their positions, and appended only to the history a reader
///     started from (ADR 0022; Profit Fox Designer ADR 0087).
/// </summary>
public sealed class SteamMarketHistoryStoreTests
{
    private ProfitGraphApiFactory _api = null!;
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
        _dataSource = NpgsqlDataSource.Create(TestDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _dataSource.DisposeAsync();
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