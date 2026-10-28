using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Tests.Unit.Domain.Notifications;

/// <summary>
///     A user's Telegram chat (ADR 0020, ADR 0042) receives notifications until the user blocks the bot.
/// </summary>
public sealed class TelegramLinkTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 28, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void NewLinkIsNotBlocked()
    {
        var userId = UserId.New();

        TelegramLink link = TelegramLink.Link(userId, chatId: 42, Now);

        link.ShouldSatisfyAllConditions(
            () => link.UserId.ShouldBe(userId),
            () => link.ChatId.ShouldBe(42),
            () => link.LinkedAt.ShouldBe(Now),
            () => link.BlockedAt.ShouldBeNull());
    }

    [Test]
    public void BlockIsRecordedOnce()
    {
        TelegramLink link = TelegramLink.Link(UserId.New(), chatId: 42, Now);

        link.Block(Now.AddHours(1)).ShouldBeTrue();
        link.Block(Now.AddHours(2)).ShouldBeFalse();

        link.BlockedAt.ShouldBe(Now.AddHours(1));
    }
}