using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Application.Bots.Runtime;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The reconnect policy of a bot's private order stream (ADR 0044): pauses grow to the last delay, an outage ends after
///     the budget, and only a restored stream that stayed up starts a new outage.
/// </summary>
public sealed class PrivateStreamOutageTests
{
    private static readonly PrivateStreamReconnectPolicy Policy = new PrivateStreamReconnectPolicy
    {
        Delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)],
        Budget = TimeSpan.FromSeconds(30),
        StableAfter = TimeSpan.FromSeconds(60)
    };

    private FakeTimeProvider _clock = null!;
    private PrivateStreamOutage _outage = null!;

    [SetUp]
    public void Create()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 12, minute: 0, second: 0, TimeSpan.Zero));
        _outage = new PrivateStreamOutage(Policy, _clock);
    }

    [Test]
    public void PausesFollowTheDelaysAndStayAtTheLast()
    {
        _outage.Lost().ShouldBe(TimeSpan.FromSeconds(1));
        _outage.Lost().ShouldBe(TimeSpan.FromSeconds(5));
        _outage.Lost().ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void AnOutageThatLastsTheBudgetEnds()
    {
        _outage.Lost().ShouldNotBeNull();
        _clock.Advance(TimeSpan.FromSeconds(29));
        _outage.Lost().ShouldNotBeNull();

        _clock.Advance(TimeSpan.FromSeconds(1));

        _outage.Lost().ShouldBeNull();
    }

    [Test]
    public void AStreamLostSoonAfterItWasRestoredContinuesTheOutage()
    {
        _outage.Lost();
        _clock.Advance(TimeSpan.FromSeconds(20));
        _outage.Restored();
        _clock.Advance(TimeSpan.FromSeconds(10));

        _outage.Lost().ShouldBeNull();
    }

    [Test]
    public void AStreamThatStayedRestoredStartsANewOutage()
    {
        _outage.Lost();
        _outage.Lost();
        _clock.Advance(TimeSpan.FromSeconds(20));
        _outage.Restored();
        _clock.Advance(TimeSpan.FromSeconds(60));

        _outage.Lost().ShouldBe(TimeSpan.FromSeconds(1));
    }
}