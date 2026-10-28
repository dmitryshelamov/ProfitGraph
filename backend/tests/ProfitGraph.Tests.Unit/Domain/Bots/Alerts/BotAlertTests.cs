using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Alerts;

namespace ProfitGraph.Tests.Unit.Domain.Bots.Alerts;

/// <summary>
///     An alert reports itself once it is raised, so it can reach Telegram (ADR 0042), and is acknowledged once.
/// </summary>
public sealed class BotAlertTests
{
    [Test]
    public void RaisingReportsTheAlert()
    {
        var id = BotAlertId.New();
        var botId = BotId.New();
        var ownerId = UserId.New();

        BotAlert alert = BotAlert.Raise(id, botId, ownerId, BotAlertKind.Degraded, "ForeignOrder", BotFixture.Created);

        alert.DomainEvents.ShouldBe([new BotAlertRaised(id, botId, ownerId, BotAlertKind.Degraded, "ForeignOrder", BotFixture.Created)]);
    }

    [Test]
    public void AlertIsAcknowledgedOnce()
    {
        BotAlert alert = BotAlert.Raise(BotAlertId.New(), BotId.New(), UserId.New(), BotAlertKind.Killed, reason: null, BotFixture.Created);

        alert.Acknowledge(BotFixture.Created.AddMinutes(1)).ShouldBeTrue();
        alert.Acknowledge(BotFixture.Created.AddMinutes(2)).ShouldBeFalse();
        alert.AcknowledgedAt.ShouldBe(BotFixture.Created.AddMinutes(1));
    }
}