using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Domain.Scanner;

public sealed class ScanPresetTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly ScanSort ByChange = new ScanSort(ScanMetric.Change1h, Descending: true);

    [Test]
    public void CreateKeepsItsDefinition()
    {
        ScanCondition[] conditions = [new ScanCondition(ScanMetric.Change1h, minimum: 3, maximum: null)];

        var preset = ScanPreset.Create(UserId.New(), new VenueId("bybit"), new ScanPresetName("  Мой импульс "), conditions, ByChange, hideStale: true, Now);

        preset.Name.Value.ShouldBe("Мой импульс");
        preset.Conditions.ShouldBe(conditions);
        preset.Sort.ShouldBe(ByChange);
        preset.HideStale.ShouldBeTrue();
        (preset.CreatedAt, preset.UpdatedAt).ShouldBe((Now, Now));
    }

    [Test]
    public void ChangeReplacesTheDefinitionAndReportsOnlyARealChange()
    {
        ScanPreset preset = Preset();
        ScanCondition[] conditions = [new ScanCondition(ScanMetric.Turnover24h, minimum: 1_000_000, maximum: null)];
        var sort = new ScanSort(ScanMetric.Turnover24h, Descending: true);

        preset.Change(new ScanPresetName("Ликвидные"), conditions, sort, hideStale: false, Now.AddHours(1)).ShouldBeTrue();
        preset.Change(new ScanPresetName("Ликвидные"), conditions, sort, hideStale: false, Now.AddHours(2)).ShouldBeFalse();

        preset.Name.Value.ShouldBe("Ликвидные");
        preset.Conditions.ShouldBe(conditions);
        preset.HideStale.ShouldBeFalse();
        preset.UpdatedAt.ShouldBe(Now.AddHours(1));
    }

    [Test]
    public void AConditionNeedsABoundAndAnOrderedRange()
    {
        Should.Throw<ScanPresetRuleException>(() => new ScanCondition(ScanMetric.Spread, minimum: null, maximum: null))
            .Code.ShouldBe(ScanPresetRuleException.InvalidCondition);
        Should.Throw<ScanPresetRuleException>(() => new ScanCondition(ScanMetric.Spread, minimum: 2, maximum: 1));
        Should.Throw<ScanPresetRuleException>(() => new ScanCondition((ScanMetric)99, minimum: 1, maximum: null));
    }

    [Test]
    public void APresetHasOneConditionPerMetricAndAtMostTwenty()
    {
        ScanPreset preset = Preset();
        ScanCondition[] twice = [new ScanCondition(ScanMetric.Spread, minimum: null, maximum: 1), new ScanCondition(ScanMetric.Spread, minimum: 0, maximum: null)];

        Should.Throw<ScanPresetRuleException>(() => preset.Change(preset.Name, twice, ByChange, hideStale: true, Now));
        Should.Throw<ScanPresetRuleException>(() => new ScanPresetName(" "));
        Should.Throw<ScanPresetRuleException>(() => new ScanPresetName(new string(c: 'a', count: 101)));
    }

    [Test]
    public void KeepsAPairWhenEveryConditionHoldsInclusively()
    {
        var preset = ScanPreset.Create(
            UserId.New(),
            new VenueId("bybit"),
            new ScanPresetName("Импульс"),
            [new ScanCondition(ScanMetric.Change1h, minimum: 3, maximum: null), new ScanCondition(ScanMetric.Spread, minimum: null, maximum: 0.1m)],
            ByChange,
            hideStale: true,
            Now);

        preset.Keeps(new ScanMetrics { Change1h = 3, Spread = 0.1m }).ShouldBeTrue();
        preset.Keeps(new ScanMetrics { Change1h = 2.99m, Spread = 0.05m }).ShouldBeFalse();
        preset.Keeps(new ScanMetrics { Change1h = 5, Spread = 0.11m }).ShouldBeFalse();
        preset.Keeps(new ScanMetrics { Change1h = 5 }).ShouldBeFalse();
        preset.Keeps(new ScanMetrics { Change1h = 5, Spread = 0.05m, Stale = true }).ShouldBeFalse();
    }

    [Test]
    public void KeepsAStalePairWhenAskedTo()
    {
        var preset = ScanPreset.Create(UserId.New(), new VenueId("bybit"), new ScanPresetName("Все"), [], ByChange, hideStale: false, Now);

        preset.Keeps(new ScanMetrics { Stale = true }).ShouldBeTrue();
    }

    private static ScanPreset Preset()
    {
        return ScanPreset.Create(
            UserId.New(),
            new VenueId("bybit"),
            new ScanPresetName("Импульс"),
            [new ScanCondition(ScanMetric.Change1h, minimum: 3, maximum: null)],
            ByChange,
            hideStale: true,
            Now);
    }
}