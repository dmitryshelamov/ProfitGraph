using System.Net;

using Dapper;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Accounts.BlockUser;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.ClaimTelegramNotifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.CreateTelegramLinkCode;
using ProfitGraph.Application.Notifications.Ports;
using ProfitGraph.Application.Notifications.RecordTelegramDelivery;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Alerts;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Infrastructure.Notifications;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Notifications;

/// <summary>
///     Telegram against a fake Bot API (ADR 0020, ADR 0042): a chat links with a one-time code and gets help; an alert
///     reaches the chat through the queue, named by its bot, and its «Принято» button acknowledges it for the chat's user
///     only; a refused message never holds up the next and a temporary failure is tried
///     again; a blocked chat receives nothing more; a blocked account neither links, acknowledges nor hears anything, even
///     what was queued before the block; an old alert is skipped; two senders never claim the same notification; and nothing
///     runs while Telegram is disabled.
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
        telegram.Push(chatId: 779, "/help");
        await RuntimeScenario.Until(() => Task.FromResult(telegram.Sent.Count >= 3), "the bot answers every chat");
        await worker.StopAsync();

        telegram.Sent.ShouldContain((777L, TelegramMessages.Linked));
        telegram.Sent.ShouldContain((778L, TelegramMessages.UnknownCode));
        telegram.Sent.ShouldContain((779L, TelegramMessages.Help));
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<long>("SELECT chat_id FROM telegram_links WHERE user_id = @id", new { id = user.Id })).ShouldBe(777);
        (await connection.ExecuteScalarAsync<long>("SELECT update_offset FROM telegram_poller_lease")).ShouldBeGreaterThan(telegram.Offsets.Min());
        telegram.Paths.ShouldAllBe(path => path.StartsWith("/bot", StringComparison.Ordinal));
    }

    [Test]
    public async Task AlertReachesTheChatNamedByItsBot()
    {
        var telegram = new FakeTelegramApi();
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost worker = Worker(telegram);
        Bot bot = (await RuntimeScenario.RunningLadders(worker, count: 1, new UserId(user.Id)))[0];
        await Settle(worker);
        await Link(worker, user, chatId: 900);
        BotAlertId alert = await Raise(worker, bot, BotAlertKind.Degraded, nameof(BotStatusReason.ForeignOrder), DateTimeOffset.UtcNow);

        await RuntimeScenario.Until(async () => await Status(alert) == nameof(TelegramNotificationStatus.Sent), "the alert is sent");
        await worker.StopAsync();

        (long chatId, string text) = telegram.Sent.Single();
        chatId.ShouldBe(900);
        text.ShouldBe("⛔ <b>Бот остановлен по безопасности</b>\nБот: BTCUSDT 1h\nПричина: на символе есть чужие заявки");
        telegram.ParseModes.Single().ShouldBe("HTML");
    }

    [Test]
    public async Task AcceptButtonAcknowledgesTheAlertOfTheChatsUserOnly()
    {
        var telegram = new FakeTelegramApi();
        UserInternal owner = await _api.CreateAccount(UserRole.User);
        UserInternal stranger = await _api.CreateAccount(UserRole.User);
        using IHost worker = Worker(telegram);
        Bot bot = (await RuntimeScenario.RunningLadders(worker, count: 1, new UserId(owner.Id)))[0];
        await Settle(worker);
        await Link(worker, owner, chatId: 905);
        await Link(worker, stranger, chatId: 906);
        BotAlertId alert = await Raise(worker, bot, BotAlertKind.Killed, nameof(BotStatusReason.Killed), DateTimeOffset.UtcNow);

        await RuntimeScenario.Until(async () => await Status(alert) == nameof(TelegramNotificationStatus.Sent), "the alert is sent");
        string data = telegram.Buttons.Single().ShouldHaveSingleItem();
        string strangers = telegram.Press(chatId: 906, messageId: 1, data);
        await RuntimeScenario.Until(() => Task.FromResult(telegram.CallbackAnswers.Any(answer => answer.CallbackId == strangers)), "the stranger's press is answered");
        bool acknowledgedByStranger = await AcknowledgedAt(alert) is not null;
        string owners = telegram.Press(chatId: 905, messageId: 1, data);
        await RuntimeScenario.Until(async () => await AcknowledgedAt(alert) is not null, "the owner's press acknowledges the alert");
        await RuntimeScenario.Until(() => Task.FromResult(telegram.CallbackAnswers.Any(answer => answer.CallbackId == owners)), "the owner's press is answered");
        await worker.StopAsync();

        data.ShouldBe($"ack:{alert.Value:D}");
        acknowledgedByStranger.ShouldBeFalse();
        telegram.CallbackAnswers.ShouldBe([(strangers, TelegramMessages.AlertNotFound), (owners, TelegramMessages.Acknowledged)]);
        telegram.RemovedButtons.ShouldBe([(905L, 1L)]);
    }

    [Test]
    public async Task RefusedMessageDoesNotHoldTheNextAndATemporaryFailureIsTriedAgain()
    {
        var telegram = new FakeTelegramApi();
        telegram.FailNextSends(
            (HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: can't parse entities"}"""),
            (HttpStatusCode.InternalServerError, string.Empty));
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost worker = Worker(telegram);
        Bot bot = (await RuntimeScenario.RunningLadders(worker, count: 1, new UserId(user.Id)))[0];
        await Settle(worker);
        await Link(worker, user, chatId: 901);
        BotAlertId refused = await Raise(worker, bot, BotAlertKind.Killed, nameof(BotStatusReason.Killed), DateTimeOffset.UtcNow.AddSeconds(-2));
        BotAlertId retried = await Raise(worker, bot, BotAlertKind.ExchangeUnavailable, reason: null, DateTimeOffset.UtcNow.AddSeconds(-1));

        await RuntimeScenario.Until(async () => await Status(retried) == nameof(TelegramNotificationStatus.Sent), "the retried alert is sent");
        await worker.StopAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (string Status, int Attempts, string? LastError) failed = await connection.QuerySingleAsync<(string, int, string?)>(
            "SELECT status, attempts, last_error FROM telegram_notifications WHERE alert_id = @id",
            new { id = refused.Value });
        failed.ShouldBe((nameof(TelegramNotificationStatus.Failed), 1, "http_400: Bad Request: can't parse entities"));
        (await connection.ExecuteScalarAsync<int>("SELECT attempts FROM telegram_notifications WHERE alert_id = @id", new { id = retried.Value })).ShouldBe(2);
        telegram.Sent.Single().Text.ShouldStartWith("📡 <b>Биржа не отвечает</b>");
    }

    [Test]
    public async Task BlockedChatReceivesNothingMore()
    {
        var telegram = new FakeTelegramApi();
        telegram.Blocked[902] = true;
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost worker = Worker(telegram);
        Bot bot = (await RuntimeScenario.RunningLadders(worker, count: 1, new UserId(user.Id)))[0];
        await Settle(worker);
        await Link(worker, user, chatId: 902);
        BotAlertId first = await Raise(worker, bot, BotAlertKind.Degraded, nameof(BotStatusReason.IntentFailed), DateTimeOffset.UtcNow);

        await RuntimeScenario.Until(async () => await Status(first) == nameof(TelegramNotificationStatus.ChatUnavailable), "the chat is unavailable");
        BotAlertId second = await Raise(worker, bot, BotAlertKind.Killed, nameof(BotStatusReason.Killed), DateTimeOffset.UtcNow);
        await RuntimeScenario.Until(OutboxDelivered, "the outbox delivers the second alert");
        await worker.StopAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<DateTime?>("SELECT blocked_at FROM telegram_links WHERE user_id = @id", new { id = user.Id })).ShouldNotBeNull();
        (await Status(second)).ShouldBeNull();
        telegram.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task BlockedAccountNeitherLinksNorAcknowledgesNorHearsNewAlerts()
    {
        var telegram = new FakeTelegramApi();
        UserInternal owner = await _api.CreateAccount(UserRole.Owner);
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost worker = Worker(telegram);
        Bot bot = (await RuntimeScenario.RunningLadders(worker, count: 1, new UserId(user.Id)))[0];
        await Settle(worker);
        await Link(worker, user, chatId: 907);
        BotAlertId sent = await Raise(worker, bot, BotAlertKind.Killed, nameof(BotStatusReason.Killed), DateTimeOffset.UtcNow);
        await RuntimeScenario.Until(async () => await Status(sent) == nameof(TelegramNotificationStatus.Sent), "the alert is sent before the block");
        TelegramLinkCodeInternal code = await Send(worker, new CreateTelegramLinkCodeCommand(user.Id));

        await Send(worker, new BlockUserCommand(owner.Id, user.Id));
        string press = telegram.Press(chatId: 907, messageId: 1, telegram.Buttons.Single().ShouldHaveSingleItem());
        telegram.Push(chatId: 908, $"/start {code.Code}");
        BotAlertId after = await Raise(worker, bot, BotAlertKind.Degraded, nameof(BotStatusReason.IntentFailed), DateTimeOffset.UtcNow);
        await RuntimeScenario.Until(() => Task.FromResult(telegram.CallbackAnswers.Any(answer => answer.CallbackId == press)), "the press is answered");
        await RuntimeScenario.Until(() => Task.FromResult(telegram.Sent.Any(message => message.ChatId == 908)), "the code is answered");
        await RuntimeScenario.Until(OutboxDelivered, "the outbox delivers the alert raised after the block");
        await worker.StopAsync();

        telegram.CallbackAnswers.ShouldBe([(press, TelegramMessages.AlertNotFound)]);
        (await AcknowledgedAt(sent)).ShouldBeNull();
        telegram.Sent.ShouldContain((908L, TelegramMessages.UnknownCode));
        (await Status(after)).ShouldBeNull();
        telegram.Sent.Count(message => message.ChatId == 907).ShouldBe(expected: 1);
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<long>("SELECT chat_id FROM telegram_links WHERE user_id = @id", new { id = user.Id })).ShouldBe(907);
    }

    [Test]
    public async Task NotificationQueuedBeforeAnAccountBlockIsNotSent()
    {
        UserInternal owner = await _api.CreateAccount(UserRole.Owner);
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost host = BotTestHost.Create(services => services.AddOutboxDispatcher());
        Bot bot = (await RuntimeScenario.RunningLadders(host, count: 1, new UserId(user.Id)))[0];
        await Settle(host);
        await Link(host, user, chatId: 909);
        BotAlertId alert = await Raise(host, bot, BotAlertKind.RiskRejection, "RiskLimit", DateTimeOffset.UtcNow);
        await RuntimeScenario.Until(async () => await Status(alert) is not null, "the alert is queued");
        await host.StopAsync();
        Guid notificationId = await NotificationId(alert);

        await Send(host, new BlockUserCommand(owner.Id, user.Id));
        TelegramClaimInternal claim = await Send(host, new ClaimTelegramNotificationsCommand(Guid.NewGuid(), Limit: 50));

        claim.Messages.ShouldNotContain(message => message.NotificationId == notificationId);
        (await Status(alert)).ShouldBe(nameof(TelegramNotificationStatus.ChatUnavailable));
    }

    [Test]
    public async Task AlertOlderThanADayIsSkipped()
    {
        var telegram = new FakeTelegramApi();
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost worker = Worker(telegram);
        Bot bot = (await RuntimeScenario.RunningLadders(worker, count: 1, new UserId(user.Id)))[0];
        await Settle(worker);
        await Link(worker, user, chatId: 903, DateTimeOffset.UtcNow.AddDays(-2));
        BotAlertId old = await Raise(worker, bot, BotAlertKind.Degraded, nameof(BotStatusReason.IntentFailed), DateTimeOffset.UtcNow.AddHours(-25));

        await RuntimeScenario.Until(async () => await Status(old) == nameof(TelegramNotificationStatus.Skipped), "the old alert is skipped");
        await worker.StopAsync();

        telegram.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task TwoSendersNeverClaimTheSameNotification()
    {
        UserInternal user = await _api.CreateAccount(UserRole.User);
        using IHost host = BotTestHost.Create(services => services.AddOutboxDispatcher());
        Bot bot = (await RuntimeScenario.RunningLadders(host, count: 1, new UserId(user.Id)))[0];
        await Settle(host);
        await Link(host, user, chatId: 904);
        BotAlertId alert = await Raise(host, bot, BotAlertKind.RiskRejection, "RiskLimit", DateTimeOffset.UtcNow);
        await RuntimeScenario.Until(async () => await Status(alert) is not null, "the alert is queued");
        await host.StopAsync();
        Guid notificationId = await NotificationId(alert);

        Guid[] claims = [Guid.NewGuid(), Guid.NewGuid()];
        TelegramClaimInternal[] taken = await Task.WhenAll(claims.Select(claim => Send(host, new ClaimTelegramNotificationsCommand(claim, Limit: 50))));

        taken.SelectMany(claim => claim.Messages).Count(message => message.NotificationId == notificationId).ShouldBe(1);
        int holder = Array.FindIndex(taken, claim => claim.Messages.Any(message => message.NotificationId == notificationId));
        TelegramOutgoingInternal message = taken[holder].Messages.Single(candidate => candidate.NotificationId == notificationId);
        message.ChatId.ShouldBe(904);
        message.Text.ShouldContain("лимит риска");
        await Send(host, new RecordTelegramDeliveryCommand(notificationId, claims[1 - holder], ChatId: 904, TelegramSendOutcome.Sent, RetryAfter: null, Error: null));
        (await Status(alert)).ShouldBe(nameof(TelegramNotificationStatus.Pending));
        await Send(host, new RecordTelegramDeliveryCommand(notificationId, claims[holder], ChatId: 904, TelegramSendOutcome.Sent, RetryAfter: null, Error: null));
        (await Status(alert)).ShouldBe(nameof(TelegramNotificationStatus.Sent));
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
                services.AddOutboxDispatcher();
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
                ["Telegram:RetryDelay"] = TimeSpan.FromMilliseconds(50).ToString(),
                ["Telegram:DeliveryInterval"] = TimeSpan.FromMilliseconds(50).ToString()
            });
    }

    /// <summary>
    ///     Starts the host and waits until the outbox delivered everything, such as the start of a new bot, so a chat linked
    ///     afterwards hears only what the test raises.
    /// </summary>
    private static async Task Settle(IHost host)
    {
        await host.StartAsync();
        await RuntimeScenario.Until(OutboxDelivered, "the outbox delivers what came before the test");
    }

    private static async Task Link(IHost host, UserInternal user, long chatId, DateTimeOffset? linkedAt = null)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>()
                        .Save(TelegramLink.Link(new UserId(user.Id), chatId, linkedAt ?? DateTimeOffset.UtcNow.AddSeconds(-1)), CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private static async Task<BotAlertId> Raise(IHost host, Bot bot, BotAlertKind kind, string? reason, DateTimeOffset at)
    {
        BotAlert alert = BotAlert.Raise(BotAlertId.New(), bot.Id, bot.OwnerId, kind, reason, at);
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

    private static async Task<string?> Status(BotAlertId alert)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<string?>("SELECT status FROM telegram_notifications WHERE alert_id = @id", new { id = alert.Value });
    }

    private static async Task<DateTime?> AcknowledgedAt(BotAlertId alert)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<DateTime?>("SELECT acknowledged_at FROM bot_alerts WHERE id = @id", new { id = alert.Value });
    }

    private static async Task<Guid> NotificationId(BotAlertId alert)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<Guid>("SELECT id FROM telegram_notifications WHERE alert_id = @id", new { id = alert.Value });
    }

    private static async Task<bool> OutboxDelivered()
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE delivered_at IS NULL AND last_error IS NULL") == 0;
    }

    private static async Task<T> Send<T>(IHost host, IRequest<T> request)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private static async Task Send(IHost host, IRequest request)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}