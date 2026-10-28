using Npgsql;

using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam.Enrollment;
using ProfitGraph.Venues.Steam.Persistence.Repositories;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     Pending authenticator links in PostgreSQL: encrypted state, moved only from the step a reader saw, and deleted.
/// </summary>
public sealed class SteamEnrollmentStoreTests
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
    public async Task APendingLinkMovesOnlyFromTheStepItWasRead()
    {
        Guid owner = (await BotSeed.NewBot()).OwnerId.Value;
        var store = new SteamEnrollmentRepository(_dataSource);
        var payload = new EncryptedSecret("test-v1", new byte[EncryptedSecret.NonceLength], [1, 2, 3], new byte[EncryptedSecret.TagLength]);
        var pending = new SteamPendingEnrollment(Guid.NewGuid(), owner, "email-code", payload, BotSeed.Now.AddMinutes(10), BotSeed.Now, BotSeed.Now);
        await store.Add(pending, CancellationToken.None);

        (await store.Transition(pending with { Step = "sign-in-pending" }, "email-code", CancellationToken.None)).ShouldBeTrue();
        (await store.Transition(pending with { Step = "adding" }, "email-code", CancellationToken.None)).ShouldBeFalse();
        SteamPendingEnrollment read = (await store.Get(pending.Id, CancellationToken.None))!;
        read.Step.ShouldBe("sign-in-pending");
        read.Payload.ShouldBe(payload);
        await store.Delete(pending.Id, CancellationToken.None);
        (await store.Get(pending.Id, CancellationToken.None)).ShouldBeNull();
    }
}