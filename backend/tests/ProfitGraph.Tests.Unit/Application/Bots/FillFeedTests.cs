using ProfitGraph.Application.Bots.RecordBotFeed;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Feed;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The feed reports a fill with its fee and the asset the fee was paid in (ADR 0063), so the page can show it in quote.
/// </summary>
public sealed class FillFeedTests
{
    [Test]
    public async Task AFillIsAFeedEntryWithItsFee()
    {
        var feed = new RecordingFeed();
        var at = new DateTimeOffset(year: 2026, month: 10, day: 30, hour: 0, minute: 10, second: 0, TimeSpan.Zero);
        var bot = BotId.New();
        var fill = BotFillId.New();

        await new RecordBotFeedHandler(feed).Handle(
            new BotFillRecorded(bot, fill, EngineOrderId: 1, OrderSide.Buy, GrossBase: 0.1m, GrossQuote: 10m, at, Fee: 0.0001m, FeeAsset.Base),
            CancellationToken.None);

        BotFeedEntry entry = feed.Entries.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(BotFeedKind.Fill);
        entry.Details.ShouldBe(
            new Dictionary<string, string>
            {
                ["fillId"] = fill.Value.ToString("D"),
                ["engineOrderId"] = "1",
                ["side"] = "Buy",
                ["grossBase"] = "0.1",
                ["grossQuote"] = "10",
                ["fee"] = "0.0001",
                ["feeAsset"] = "Base"
            },
            ignoreOrder: true);
    }
}