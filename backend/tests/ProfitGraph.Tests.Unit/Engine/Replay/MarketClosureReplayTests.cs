using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

/// <summary>
///     Replay history with market closures (ADR 0023, TC1): a gap is valid only where closures cover it completely.
/// </summary>
public sealed class MarketClosureReplayTests
{
    private static readonly Timeframe Hour = Timeframe.Parse("1h");

    [Test]
    public void GapCoveredByClosures_IsAccepted_AndSlicesKeepThem()
    {
        MarketClosure[] closures = [Closure(from: 2, to: 3), Closure(from: 3, to: 5)];

        var prepared = new PreparedReplay(Hour, [Bar(0)], [Bar(1), Bar(5), Bar(6)], closures);

        prepared.Closures.ShouldBe(closures);
        prepared.Slice(2).Closures.ShouldBe(closures);
        prepared.Window(start: 1, count: 2).Closures.ShouldBe(closures);
        PreparedReplay.FromOwnedHistory(Hour, [Bar(0), Bar(1), Bar(5)], warmupCount: 1, closures).Replay.Count.ShouldBe(2);
    }

    [Test]
    public void HistoryWithoutClosures_StillMustBeContiguous()
    {
        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(2)]));
        Should.Throw<ArgumentException>(() => PreparedReplay.FromOwnedHistory(Hour, [Bar(0), Bar(2)], warmupCount: 0));
    }

    [Test]
    public void PartlyCoveredGap_IsRejected()
    {
        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(4)], [Closure(from: 1, to: 2)]));
        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(4)], [Closure(from: 1, to: 2), Closure(from: 3, to: 4)]));
    }

    [Test]
    public void CandleInsideAClosure_IsRejected()
    {
        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(1)], [Closure(from: 1, to: 2)]));
        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(3)], [Closure(from: 1, to: 4)]));
    }

    [Test]
    public void InvalidClosures_AreRejected()
    {
        MarketClosure misaligned = Closure(from: 1, to: 2) with { From = DateTimeOffset.UnixEpoch.AddMinutes(90) };

        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(3)], [misaligned]));
        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(3)], [Closure(from: 1, to: 3), Closure(from: 2, to: 3)]));
        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(3)], [Closure(from: 2, to: 3), Closure(from: 1, to: 2)]));
        Should.Throw<ArgumentException>(() => new PreparedReplay(Hour, [], [Bar(0), Bar(1)], [Closure(from: 2, to: 2)]));
    }

    private static MarketClosure Closure(int from, int to)
    {
        return new MarketClosure(DateTimeOffset.UnixEpoch.AddHours(from), DateTimeOffset.UnixEpoch.AddHours(to));
    }
}