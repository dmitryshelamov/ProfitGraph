using ProfitGraph.Application.Bots.RecordBotFeed;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Feed;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The feed reports why a bot degraded, with the runtime's safe diagnostic text when there is one, and a restated candle
///     with both closes as exact decimals (ADR 0029).
/// </summary>
public sealed class DegradedFeedTests
{
    private static readonly DateTimeOffset At = new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public async Task ADegradationWithADetailIsAFeedEntryWithTheDetail()
    {
        var feed = new RecordingFeed();

        await new RecordBotFeedHandler(feed).Handle(
            new BotDegraded(BotId.New(), BotStatusReason.RuntimeFailure, At, "InvalidOperationException: A gap."),
            CancellationToken.None);

        feed.Entries.ShouldHaveSingleItem().Details.ShouldBe(
            new Dictionary<string, string> { ["reason"] = "RuntimeFailure", ["detail"] = "InvalidOperationException: A gap." },
            ignoreOrder: true);
    }

    [Test]
    public async Task ADegradationWithoutADetailKeepsOnlyItsReason()
    {
        var feed = new RecordingFeed();

        await new RecordBotFeedHandler(feed).Handle(new BotDegraded(BotId.New(), BotStatusReason.ForeignOrder, At), CancellationToken.None);

        feed.Entries.ShouldHaveSingleItem().Details.ShouldBe(new Dictionary<string, string> { ["reason"] = "ForeignOrder" });
    }

    [Test]
    public async Task ARestatedCandleIsAFeedEntryWithBothCloses()
    {
        var feed = new RecordingFeed();

        await new RecordBotFeedHandler(feed).Handle(
            new BotHistoryRestated(BotId.New(), At.AddHours(-1), PreviousClose: 1200m, RestatedClose: 1250.5m, At),
            CancellationToken.None);

        BotFeedEntry entry = feed.Entries.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(BotFeedKind.HistoryRestated);
        entry.Details.ShouldBe(
            new Dictionary<string, string> { ["openTime"] = "2026-10-27T09:00:00.0000000+00:00", ["previousClose"] = "1200", ["restatedClose"] = "1250.5" },
            ignoreOrder: true);
    }
}
