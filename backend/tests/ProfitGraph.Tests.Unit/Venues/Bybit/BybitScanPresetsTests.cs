using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Venues.Bybit;
using ProfitGraph.Venues.Bybit.Scanner;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

public sealed class BybitScanPresetsTests
{
    private static readonly string[] Names = ["Импульс", "Ранний импульс", "Падение", "Раннее падение", "Под лесенку"];

    [Test]
    public void SpotAndPerpetualsHaveTheSamePresets()
    {
        BybitScanPresets.For(BybitMarket.Spot).Select(preset => preset.Name).ShouldBe(Names);
        BybitScanPresets.For(BybitMarket.Linear).Select(preset => preset.Name).ShouldBe(Names);
        BybitScanPresets.For(BybitMarket.Spot).ShouldAllBe(preset => preset.Venue == BybitMarket.Spot.Venue.Value && preset.BuiltIn && !preset.AlertsEnabled);
        BybitScanPresets.For(BybitMarket.Linear).ShouldAllBe(preset => preset.Venue == BybitMarket.Linear.Venue.Value && preset.BuiltIn && !preset.AlertsEnabled);
    }

    [Test]
    public void PerpetualsAskForTwiceTheTurnoverWithTheSameOtherConditions()
    {
        foreach ((ScanPresetInternal spot, ScanPresetInternal linear) in BybitScanPresets.For(BybitMarket.Spot).Zip(BybitScanPresets.For(BybitMarket.Linear)))
        {
            Turnover(linear).ShouldBe(Turnover(spot) * 2m);
            linear.Conditions.Where(condition => condition.Metric != "Turnover24h").ShouldBe(spot.Conditions.Where(condition => condition.Metric != "Turnover24h"));
            (linear.SortMetric, linear.SortDescending).ShouldBe((spot.SortMetric, spot.SortDescending));
        }
    }

    [Test]
    public void TheRisesTradeLongAndTheFallsShort()
    {
        BybitScanPresets.For(BybitMarket.Linear)
            .Select(preset => preset.TradeDirection)
            .ShouldBe([ScanTradeDirection.Long, ScanTradeDirection.Long, ScanTradeDirection.Short, ScanTradeDirection.Short, ScanTradeDirection.Both]);
    }

    [Test]
    public void TheFallsMirrorTheRises()
    {
        IReadOnlyList<ScanPresetInternal> presets = BybitScanPresets.For(BybitMarket.Spot);
        ScanPresetInternal early = presets.Single(preset => preset.Name == "Раннее падение");

        presets.Single(preset => preset.Name == "Падение").Conditions.ShouldContain(new ScanConditionInternal("Change1h", Minimum: null, Maximum: -3m));
        early.Conditions.ShouldContain(new ScanConditionInternal("Change15m", Minimum: null, Maximum: -2m));
        early.Conditions.ShouldContain(new ScanConditionInternal("Breakdown4h", Minimum: null, Maximum: 0m));
        early.Conditions.ShouldContain(new ScanConditionInternal("Change1h", Minimum: -6m, Maximum: null));
        early.SortDescending.ShouldBeFalse();
    }

    private static decimal Turnover(ScanPresetInternal preset)
    {
        return preset.Conditions.Single(condition => condition.Metric == "Turnover24h").Minimum!.Value;
    }
}
