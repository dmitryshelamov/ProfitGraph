using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Application.Optimization;

internal static class OptimizationRunnerFixture
{
    public static OptimizationRunInputInternal Sma(int maxFast = 3, int maxSlow = 4)
    {
        StrategyGraph source = RawSmaStrategyGraphFactory.Create();
        IEnumerable<GraphNode> nodes = source.Nodes.Select(node =>
        {
            if (node.Id is not ("fastPeriod" or "slowPeriod"))
            {
                return node;
            }

            var parameters = new Dictionary<string, ParamValue>(node.Parameters)
            {
                ["value"] = new DecimalOptimizeParamValue(node.Id == "fastPeriod" ? 1m : 2m, node.Id == "fastPeriod" ? maxFast : maxSlow, Step: 1m)
            };

            return new GraphNode(node.Id, node.TypeKey, node.SchemaVersion, parameters, node.DeclaredValueType, node.EvaluationMode);
        });

        return Create(new StrategyGraph(nodes, source.Connections, source.TemplateKind));
    }

    public static OptimizationRunInputInternal Create(StrategyGraph graph)
    {
        INodeDefinition[] definitions = EngineGraphFixture.Definitions
            .Concat(Enum.GetValues<TradingNodeBehavior>().Select(behavior => new TradingNodeDefinition(behavior)))
            .ToArray();
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, definitions);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph compiled = compilation.Graph.ShouldNotBeNull();

        return new OptimizationRunInputInternal(
            graph.TemplateKind,
            compiled,
            History(4m, 3m, 2m, 5m, 6m, 2m, 1m, 4m, 6m, 2m, 5m, 3m),
            InSampleBars: 8,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            OptimizationSearchSpace.FromGraph(compiled),
            Constraints: new OptimizationConstraints(minExecutions: 0));
    }

    private static PreparedReplay History(params decimal[] prices)
    {
        Candle[] candles = prices.Select((price, index) => EngineGraphFixture.Candle(index, price))
            .ToArray();

        return new PreparedReplay(Timeframe.Parse("1h"), Array.Empty<Candle>(), candles);
    }
}