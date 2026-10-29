using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Application.Scanner;
using ProfitGraph.Venues.TInvest.Scanner;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     The scanner's share of a T-Invest per-minute limit (ADR 0062): turns are spaced by their units, and a pause refuses them.
/// </summary>
public sealed class TInvestScanPacerTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 7, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public async Task ATurnOfSeveralUnitsHoldsTheNextOneBackForAsLong()
    {
        var clock = new FakeTimeProvider(Start);
        var pacer = new TInvestScanPacer(perMinute: 50, clock);

        await pacer.Take(units: 50, CancellationToken.None);
        Task next = pacer.Take(units: 1, CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(millisecondsDelay: 20);
        next.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task APauseRefusesTurnsUntilItEnds()
    {
        var clock = new FakeTimeProvider(Start);
        var pacer = new TInvestScanPacer(perMinute: 600, clock);
        pacer.Pause(TimeSpan.FromMinutes(1));

        var failure = await Should.ThrowAsync<MarketScanException>(() => pacer.Take(units: 1, CancellationToken.None));
        failure.Code.ShouldBe(MarketScanErrorCodes.RequestsPaused);
        pacer.PausedUntil.ShouldBe(Start.AddMinutes(1));

        clock.Advance(TimeSpan.FromMinutes(1));
        await pacer.Take(units: 1, CancellationToken.None);
        pacer.PausedUntil.ShouldBeNull();
    }
}