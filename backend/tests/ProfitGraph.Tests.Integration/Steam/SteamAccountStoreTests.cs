using Npgsql;

using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Persistence.Repositories;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     Steam accounts awaiting their eligibility check in PostgreSQL: due when their check time passed, moved only from the
///     status a checker read (ADR 0022).
/// </summary>
public sealed class SteamAccountStoreTests
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
    public async Task ACheckingAccountIsDueUntilItsStatusMoves()
    {
        Guid credential = (await BotSeed.NewBot()).CredentialId.Value;
        var store = new SteamAccountRepository(_dataSource);
        (await store.Add(SteamAccount.Imported(credential, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), BotSeed.Now), CancellationToken.None))
            .ShouldBeTrue();

        (await store.Due(BotSeed.Now, CancellationToken.None)).Select(account => account.CredentialId).ShouldContain(credential);
        (await store.Record(credential, SteamGuardStatus.Checking, SteamGuardStatus.Checking, BotSeed.Now.AddMinutes(15), BotSeed.Now, CancellationToken.None))
            .ShouldBeTrue();
        (await store.Due(BotSeed.Now.AddMinutes(1), CancellationToken.None)).Select(account => account.CredentialId).ShouldNotContain(credential);
        (await store.Record(credential, SteamGuardStatus.Checking, SteamGuardStatus.Ready, nextCheckAt: null, BotSeed.Now, CancellationToken.None)).ShouldBeTrue();
        (await store.Record(credential, SteamGuardStatus.Checking, SteamGuardStatus.Restricted, nextCheckAt: null, BotSeed.Now, CancellationToken.None)).ShouldBeFalse();

        (await store.Get(credential, CancellationToken.None))!.GuardStatus.ShouldBe(SteamGuardStatus.Ready);
        (await store.Due(BotSeed.Now.AddDays(1), CancellationToken.None)).Select(account => account.CredentialId).ShouldNotContain(credential);
    }
}