using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class IndicatorTemplateLayoutTests
{
    private static readonly object[] Templates =
    [
        new object[] { EmaCrossoverStrategyBlockFactory.Create(), "ema", 27 },
        new object[] { MacdTrendStrategyBlockFactory.Create(), "macd", 200 },
        new object[] { RsiReversionStrategyBlockFactory.Create(), "rsi", 16 },
        new object[] { BollingerReversionStrategyBlockFactory.Create(), "bollinger", 20 }
    ];

    [TestCaseSource(nameof(Templates))]
    public void TemplateBuysAndSellsOnePositionByMarketOnClosedCandles(StrategyDefinition definition, string tag, int warmupCandles)
    {
        definition.Blocks
            .Select(block => (block.Id, block.Kind))
            .ShouldBe(
            [
                ("clock", "event"), ("entryConditions", "condition"), ("exitConditions", "condition"), ("exitSignal", "condition"), ("entry", "order"),
                ("exit", "order")
            ]);
        definition.Blocks[0].Source.ShouldBe("OnCandleClosed");
        definition.Wires
            .Select(wire => $"{wire.From.Block}.{wire.From.Exit}->{wire.To}")
            .ShouldBe(
            [
                "clock.out->entryConditions", "entryConditions.yes->entry", "clock.out->exitConditions", "exitConditions.yes->exitSignal",
                "exitSignal.yes->exit"
            ]);

        StrategyBlock entryConditions = definition.Blocks.Single(block => block.Id == "entryConditions");
        entryConditions.Join.ShouldBe("all");
        entryConditions.Checks.Select(check => check.Type).ShouldContain("PositionFlat");
        entryConditions.Checks.Select(check => check.Type).ShouldContain("NoActiveOrders");
        StrategyBlock exitSignal = definition.Blocks.Single(block => block.Id == "exitSignal");
        exitSignal.Join.ShouldBe("any");

        // Every template carries the stop: close at or below the last buy price lowered by stopLoss.
        exitSignal.Checks.ShouldContain(check => check.Type == "Compare" && check.Operation == "<=" && check.Operands["b"].Name == "stopPrice");
        definition.Parameters.Single(parameter => parameter.Name == "stopLoss").DefaultValue.ShouldBe(0.05m);

        StrategyBlock entry = definition.Blocks.Single(block => block.Id == "entry");
        entry.OrderKind.ShouldBe("Market");
        entry.Side.ShouldBe("Buy");
        entry.Tag!.Text.ShouldBe($"{tag}.entry");
        entry.Amount!.Name.ShouldBe("buyAmount");
        StrategyBlock exit = definition.Blocks.Single(block => block.Id == "exit");
        exit.OrderKind.ShouldBe("Market");
        exit.Side.ShouldBe("Sell");
        exit.Tag!.Text.ShouldBe($"{tag}.exit");
        exit.Quantity!.Field.ShouldBe("quantity");

        IndicatorTemplateReplay.Compile(definition).WarmupCandles.ShouldBe(warmupCandles);
    }
}