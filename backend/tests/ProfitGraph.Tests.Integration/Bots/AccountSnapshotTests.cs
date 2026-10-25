using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.RecordAccountBalance;
using ProfitGraph.Application.Accounts.ThinAccountSnapshots;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Account value snapshots (stage 5.14, Profit Fox Designer ADR 0069): one per credential and five-minute bucket, a
///     failed read keeps the last one, old history is thinned to hourly, and the Worker reads every credential with a bot.
///     It lives with the bot tests because every credential it reads belongs to a bot.
/// </summary>
[NonParallelizable]
public sealed class AccountSnapshotTests
{
    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task SecondObservationInABucketIsDroppedAndAFailureKeepsTheLastOne()
    {
        using IHost host = BotTestHost.Create();
        Bot bot = await RuntimeScenario.RunningLadder(host);
        var bucket = new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks - DateTimeOffset.UtcNow.UtcTicks % AccountSnapshot.Bucket.Ticks, TimeSpan.Zero);

        bool first = await Record(host, bot, Observed(totalValueUsd: 100m, bucket.AddSeconds(10)));
        bool duplicate = await Record(host, bot, Observed(totalValueUsd: 101m, bucket.AddSeconds(20)));
        await Record(host, bot, AccountBalanceReadInternal.Failed("venue_unavailable", "503"));

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (first, duplicate).ShouldBe((true, false));
        (await connection.QueryAsync<decimal>("SELECT total_value_usd FROM account_value_snapshots WHERE credential_id = @id", new { id = bot.CredentialId.Value }))
            .ShouldBe([100m]);
        var status = await connection.QuerySingleAsync<(DateTime? LastSuccessAt, string? FailureReason, int Failures)>(
            "SELECT last_success_at, failure_reason, consecutive_failures FROM account_balance_status WHERE credential_id = @id",
            new { id = bot.CredentialId.Value });
        status.LastSuccessAt.ShouldNotBeNull();
        status.FailureReason.ShouldBe("venue_unavailable");
        status.Failures.ShouldBe(1);
    }

    [Test]
    public async Task HistoryOlderThanAWeekKeepsTheLatestSnapshotOfEachHour()
    {
        using IHost host = BotTestHost.Create();
        Bot bot = await RuntimeScenario.RunningLadder(host);
        DateTimeOffset hour = new DateTimeOffset(DateTimeOffset.UtcNow.AddDays(-10).Date, TimeSpan.Zero).AddHours(10);
        await Record(host, bot, Observed(totalValueUsd: 1m, hour.AddMinutes(5)));
        await Record(host, bot, Observed(totalValueUsd: 2m, hour.AddMinutes(35)));
        await Record(host, bot, Observed(totalValueUsd: 3m, hour.AddMinutes(65)));
        await Record(host, bot, Observed(totalValueUsd: 4m, DateTimeOffset.UtcNow.AddMinutes(-15)));
        await Record(host, bot, Observed(totalValueUsd: 5m, DateTimeOffset.UtcNow.AddMinutes(-5)));

        using (IServiceScope scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ThinAccountSnapshotsCommand());
        }

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.QueryAsync<decimal>(
                "SELECT total_value_usd FROM account_value_snapshots WHERE credential_id = @id ORDER BY captured_at",
                new { id = bot.CredentialId.Value }))
            .ShouldBe([2m, 3m, 4m, 5m]);
    }

    [Test]
    public async Task WorkerReadsEveryCredentialWithABotAndRecordsWhatHappened()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await BotStore.Change(worker, bot.Id, stored => stored.Stop(DateTimeOffset.UtcNow));

        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () =>
            {
                await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

                return await connection.ExecuteScalarAsync<string?>(
                    "SELECT failure_reason FROM account_balance_status WHERE credential_id = @id",
                    new { id = bot.CredentialId.Value }) is not null;
            },
            "the credential's balance is read");
        await worker.StopAsync();
    }

    private static AccountBalanceReadInternal Observed(decimal totalValueUsd, DateTimeOffset at)
    {
        return AccountBalanceReadInternal.Observed(
            new AccountBalanceObservationInternal(totalValueUsd, AvailableValueUsd: null, [new AccountCoinBalanceInternal("USDT", totalValueUsd, totalValueUsd)], at));
    }

    private static async Task<bool> Record(IHost host, Bot bot, AccountBalanceReadInternal read)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordAccountBalanceCommand(bot.CredentialId.Value, read));
    }
}