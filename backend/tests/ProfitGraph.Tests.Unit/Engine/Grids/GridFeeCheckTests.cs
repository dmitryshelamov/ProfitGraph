using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Grids;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Grids;

/// <summary>
///     ADR 0058, section 8: a profit step at or below the round trip's fees is an error, a step below them plus slippage a warning.
/// </summary>
public sealed class GridFeeCheckTests
{
    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.0001m,
        priceTick: 0.01m,
        minQuantity: 0m,
        minNotional: 1m,
        basePrecision: 4,
        quotePrecision: 2);

    [TestCase(0.002, 0.01, 0, true, GridFeeFinding.ProfitBelowFees)]
    [TestCase(0.0021, 0.01, 0.001, false, GridFeeFinding.StepNearFees)]
    [TestCase(0.01, 0.0025, 0.001, false, GridFeeFinding.StepNearFees)]
    [TestCase(0.01, 0.01, 0.001, false, null)]
    public void ComparesTheStepsWithTwoLimitFeesAndSlippage(decimal profitStep, decimal step, decimal slippage, bool error, string? code)
    {
        IReadOnlyList<GridFeeFinding> findings = GridFeeCheck.Check(
            Compile(profitStep, step),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage, limitFeeRate: 0.001m, marketFeeRate: 0.001m));

        if (code is null)
        {
            findings.ShouldBeEmpty();
        }
        else
        {
            GridFeeFinding finding = findings.ShouldHaveSingleItem();
            finding.Code.ShouldBe(code);
            finding.IsError.ShouldBe(error);
        }
    }

    [Test]
    public void SellerFeesArePricedAtTheReferencePrice()
    {
        var settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0m, new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m));

        GridFeeCheck.Check(Compile(profitStep: 0.1m, step: 0.2m), EngineExecutionInputs.Empty, settings).ShouldBeEmpty();
        GridFeeCheck.Check(Compile(profitStep: 0.1m, step: 0.2m), EngineExecutionInputs.Empty, settings, Rules, referencePrice: 100m)
            .ShouldHaveSingleItem()
            .IsError.ShouldBeTrue();
    }

    [Test]
    public void AReplayWithALosingProfitStepFailsBeforeItsFirstCandle()
    {
        var candle = new Candle(
            new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero),
            Open: 100m,
            High: 100m,
            Low: 90m,
            Close: 95m,
            Volume: 1m);
        var input = new BacktestInput(
            Compile(profitStep: 0.001m, step: 0.01m),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            Rules,
            Timeframe.Parse("1h"),
            [],
            [candle]);

        BacktestResult result = new BacktestEngine().Run(input);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Failed);
        result.Terminal.Diagnostic!.Code.ShouldBe(GridFeeFinding.ProfitBelowFees);
        result.FillCount.ShouldBe(0);
    }

    private static CompiledTradingGraph Compile(decimal profitStep, decimal step)
    {
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(GridStrategyBlockFactory.Create(step: step, profitStep: profitStep));
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }
}