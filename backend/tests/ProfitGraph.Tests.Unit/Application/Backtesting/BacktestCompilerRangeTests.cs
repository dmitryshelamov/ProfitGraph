using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Application.Strategies;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Application.Backtesting;

public sealed class BacktestCompilerRangeTests
{
    private static readonly BacktestCompiler Compiler = new BacktestCompiler(new TestNodeTypeRegistry(ProductionTradingNodeTests.Catalog));

    [Test]
    public void RangedParameter_BecomesASearchDimensionKeyedByItsName()
    {
        CompiledTradingGraph graph = Compiler.Compile(
            SmaStrategyBlockFactory.Create(),
            new Dictionary<string, decimal> { ["slowPeriod"] = 30m },
            new Dictionary<string, DecimalOptimizeParamValue> { ["fastPeriod"] = new DecimalOptimizeParamValue(Min: 5m, Max: 15m, Step: 5m) });
        OptimizationSearchSpace space = OptimizationSearchSpace.FromGraph(graph);
        OptimizationParameterRange range = space.Parameters.ShouldHaveSingleItem();
        range.Reference.ShouldBe(new PortReference("fastPeriod", "value"));
        space.GridSize.ShouldBe(3);
    }

    [Test]
    public void UnknownOrFixedRange_IsRejectedOnTheRangeField()
    {
        var error = Should.Throw<ApplicationValidationException>(() => Compiler.Compile(
            SmaStrategyBlockFactory.Create(),
            new Dictionary<string, decimal> { ["fastPeriod"] = 10m },
            new Dictionary<string, DecimalOptimizeParamValue>
            {
                ["fastPeriod"] = new DecimalOptimizeParamValue(Min: 5m, Max: 15m, Step: 5m),
                ["missing"] = new DecimalOptimizeParamValue(Min: 1m, Max: 2m, Step: 1m)
            }));
        error.Errors.Select(item => (item.Code, item.Field))
            .ShouldBe(
                [(BacktestErrorCodes.UnknownParameter, "Ranges.missing"), (BacktestErrorCodes.ParameterFixedAndRanged, "Ranges.fastPeriod")],
                ignoreOrder: true);
    }
}