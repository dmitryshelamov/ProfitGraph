using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Feed;

namespace ProfitGraph.Tests.Unit.Domain.Bots.Feed;

public sealed class BotFeedEntryTests
{
    [Test]
    public void EntryKeepsItsOwnCopyOfTheDetails()
    {
        var details = new Dictionary<string, string> { ["reason"] = "ForeignOrder" };
        BotFeedEntry entry = BotFeedEntry.Record(BotFeedEntryId.New(), BotId.New(), BotFeedKind.Degraded, details, BotFixture.Created);

        details["reason"] = "changed";
        details["extra"] = "added";

        entry.Details.ShouldBe(new Dictionary<string, string> { ["reason"] = "ForeignOrder" });
    }
}