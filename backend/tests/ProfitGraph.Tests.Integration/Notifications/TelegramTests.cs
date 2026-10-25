using Dapper;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.CreateTelegramLinkCode;
using ProfitGraph.Application.Notifications.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Infrastructure.Notifications;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Notifications;

/// <summary>
///     Telegram alerts against a fake Bot API (stage 5.15, ADR 0020): a chat links with a one-time code, alerts reach it at
///     least once and again after a failed send, a blocked chat receives nothing more, and nothing runs while Telegram is
///     disabled.
/// </summary>
[NonParallelizable]
public sealed class TelegramTests
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

    [SetUp]
    public async Task FreeTheLease()
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("DELETE FROM telegram_poller_lease");
    }

    [Test]
    public async Task ChatLinksWithACodeAndTheOffsetMovesOn()
    {
        var telegram = new FakeTelegramApi();
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost worker = Worker(telegram);
        TelegramLinkCodeInternal code = await Send(worker, new CreateTelegramLinkCodeCommand(user.Id));

        await worker.StartAsync();
        telegram.Push(chatId: 777, $"/start {code.Code}");
        telegram.Push(chatId: 778, "/start WRONGCODE1");
        await RuntimeScenario.Until(() => Task.FromResult(telegram.Sent.Count >= 2), "the bot answers both chats");
        await worker.StopAsync();

        telegram.Sent.ShouldContain((777L, TelegramMessages.Linked));
        telegram.Sent.ShouldContain((778L, TelegramMessages.UnknownCode));
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<long>("SELECT chat_id FROM telegram_links WHERE user_id = @id", new { id = user.Id })).ShouldBe(777);
        (await connection.ExecuteScalarAsync<long>("SELECT update_offset FROM telegram_poller_lease")).ShouldBeGreaterThan(telegram.Offsets.Min());
        telegram.Paths.ShouldAllBe(path => path.StartsWith("/bot", StringComparison.Ordinal));
    }

    [Test]
    public async Task AlertReachesTheChatAgainAfterAFailedSendAndABlockedChatGetsNoMore()
    {
        var telegram = new FakeTelegramApi();
        telegram.FailSends(count: 2);
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost worker = Worker(telegram);
        Bot bot = (await RuntimeScenario.RunningLadders(worker, count: 1, new UserId(user.Id)))[0];
        await Link(worker, user, chatId: 900);
        BotAlertId first = await Raise(worker, bot, BotAlertKind.Degraded);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await SentAt(first) is not null, "the alert is delivered");
        telegram.Blocked[900] = true;
        BotAlertId second = await Raise(worker, bot, BotAlertKind.Killed);
        await RuntimeScenario.Until(async () => await BlockedAt(user) is not null, "the chat is marked blocked");
        await worker.StopAsync();

        telegram.Sent.Count(sent => sent.ChatId == 900).ShouldBe(1);
        telegram.Sent.Single().Text.ShouldContain("BTCUSDT 1h");
        (await SentAt(second)).ShouldBeNull();
    }

    [Test]
    public void DisabledTelegramStartsNothing()
    {
        IServiceCollection services = new ServiceCollection().AddTelegramNotifications(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:Enabled"] = "false" }).Build());

        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(ITelegramClient));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    private static IHost Worker(FakeTelegramApi telegram)
    {
        return BotTestHost.Create(
            services =>
            {
                services.AddTelegramNotifications(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:Enabled"] = "true" }).Build());
                services.AddHttpClient(nameof(ITelegramClient)).ConfigurePrimaryHttpMessageHandler(() => telegram);
            },
            settings: new Dictionary<string, string?>
            {
                ["Telegram:Enabled"] = "true",
                ["Telegram:BotToken"] = FakeTelegramApi.Token,
                ["Telegram:BaseAddress"] = "https://telegram.invalid/",
                ["Telegram:PollTimeout"] = TimeSpan.Zero.ToString(),
                ["Telegram:RetryDelay"] = TimeSpan.FromMilliseconds(50).ToString()
            });
    }

    private static async Task Link(IHost host, UserInternal user, long chatId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>()
                        .Save(TelegramLink.Link(new UserId(user.Id), chatId, DateTimeOffset.UtcNow.AddSeconds(-1)), CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private static async Task<BotAlertId> Raise(IHost host, Bot bot, BotAlertKind kind)
    {
        BotAlert alert = BotAlert.Raise(BotAlertId.New(), bot.Id, bot.OwnerId, kind, reason: null, DateTimeOffset.UtcNow);
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotAlertRepository>().Add(alert, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return alert.Id;
    }

    private static async Task<DateTime?> SentAt(BotAlertId alert)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<DateTime?>("SELECT telegram_sent_at FROM bot_alerts WHERE id = @id", new { id = alert.Value });
    }

    private static async Task<DateTime?> BlockedAt(UserInternal user)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<DateTime?>("SELECT blocked_at FROM telegram_links WHERE user_id = @id", new { id = user.Id });
    }

    private static async Task<T> Send<T>(IHost host, IRequest<T> request)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}