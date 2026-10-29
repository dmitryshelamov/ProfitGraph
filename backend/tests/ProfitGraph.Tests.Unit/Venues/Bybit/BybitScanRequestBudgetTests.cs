using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Application.Scanner;
using ProfitGraph.Venues.Bybit.MarketData;
using ProfitGraph.Venues.Bybit.Scanner;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

public sealed class BybitScanRequestBudgetTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public async Task RequestsStartOneSpacingApart()
    {
        var clock = new FakeTimeProvider(Start);
        var budget = new BybitScanRequestBudget(new BybitOptions { ScannerRequestsPerSecond = 10 }, clock);

        await budget.Take(CancellationToken.None);
        Task second = budget.Take(CancellationToken.None);

        second.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(99));
        second.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await second;
    }

    [Test]
    public async Task APauseRefusesRequestsUntilItEnds()
    {
        var clock = new FakeTimeProvider(Start);
        var budget = new BybitScanRequestBudget(new BybitOptions { ScannerBanPause = TimeSpan.FromMinutes(10) }, clock);

        budget.Pause();

        budget.PausedUntil.ShouldBe(Start.AddMinutes(10));
        var refused = await Should.ThrowAsync<MarketScanException>(() => budget.Take(CancellationToken.None));
        refused.Code.ShouldBe(MarketScanErrorCodes.RequestsPaused);

        clock.Advance(TimeSpan.FromMinutes(10));

        budget.PausedUntil.ShouldBeNull();
        await budget.Take(CancellationToken.None);
    }
}