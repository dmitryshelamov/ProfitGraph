using ProfitGraph.Application.Bots.RecordBotFeed;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The feed reports a late commission with the order and both fees as exact decimals (ADR 0023, TC4).
/// </summary>
public sealed class FeeCorrectionFeedTests
{
    [Test]
    public async Task AFeeCorrectionIsAFeedEntryWithBothFees()
    {
        var feed = new RecordingFeed();
        var at = new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 10, minute: 0, second: 0, TimeSpan.Zero);
        var bot = BotId.New();

        await new RecordBotFeedHandler(feed).Handle(
            new BotFeeCorrected(bot, BotFeeCorrectionId.New(), EngineOrderId: 7, PreviousFee: 0m, CorrectedFee: 1.50m, at),
            CancellationToken.None);

        BotFeedEntry entry = feed.Entries.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(BotFeedKind.FeeCorrected);
        entry.BotId.ShouldBe(bot);
        entry.Details.ShouldBe(new Dictionary<string, string> { ["engineOrderId"] = "7", ["previousFee"] = "0", ["correctedFee"] = "1.5" }, ignoreOrder: true);
    }
}