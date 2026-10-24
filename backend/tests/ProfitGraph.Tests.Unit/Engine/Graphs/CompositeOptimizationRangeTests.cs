using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Composites;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Engine.Replay;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class CompositeOptimizationRangeTests
{
    private static readonly IReadOnlyList<INodeDefinition> Definitions = EngineGraphFixture.Definitions
        .Concat(Enum.GetValues<TradingNodeBehavior>().Select(behavior => new TradingNodeDefinition(behavior)))
        .ToArray();

    [Test]
    public void DecimalExposedRange_PreservesTrialResultsAndExternalIdentity()
    {
        StrategyGraph graph = SmaCrossover(new DecimalOptimizeParamValue(Min: 0m, Max: 100m, Step: 100m));
        StrategyGraph expanded = new CompositeExpander().Expand(graph, Definitions).Graph;
        var flat = new StrategyGraph(
            expanded.Nodes.Select(node => new GraphNode(node.Id, node.TypeKey, node.SchemaVersion, node.Parameters, node.DeclaredValueType, node.EvaluationMode)),
            expanded.Connections,
            expanded.TemplateKind);
        CompiledTradingGraph compositePlan = Compile(graph);
        CompiledTradingGraph flatPlan = Compile(flat);
        OptimizationSearchSpace compositeSpace = OptimizationSearchSpace.FromGraph(compositePlan);
        OptimizationSearchSpace flatSpace = OptimizationSearchSpace.FromGraph(flatPlan);
        compositeSpace.Parameters.Single().Reference.ShouldBe(new PortReference("positiveBase", "minQuantity"));
        compositeSpace.GridSize.ShouldBe(flatSpace.GridSize);
        OptimizationTrial[] outerTrials = compositeSpace.Enumerate().ToArray();
        OptimizationTrial[] flatTrials = flatSpace.Enumerate().ToArray();
        outerTrials.Length.ShouldBe(2);

        for (var index = 0; index < outerTrials.Length; index++)
        {
            BacktestResult actual = Run(compositePlan, outerTrials[index].Selections);
            BacktestResult expected = Run(flatPlan, flatTrials[index].Selections);
            actual.Ledger.ShouldBe(expected.Ledger);
            actual.Terminal.TotalPnl.ShouldBe(expected.Terminal.TotalPnl);
        }
    }

    private static StrategyGraph SmaCrossover(ParamValue minQuantity)
    {
        var nodes = new List<GraphNode>();
        var wires = new List<GraphConnection>();
        Parameter("fastPeriod", ValueType.Integer, value: 2m);
        Parameter("slowPeriod", ValueType.Integer, value: 3m);
        Parameter("buyAmount", ValueType.QuoteAmount, value: 100m);
        Node("close", "CandleField", new Dictionary<string, ParamValue> { ["field"] = EngineGraphFixture.Text("Close") });
        Node("fast", "SMA", new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("fastPeriod", "out")) });
        Node("slow", "SMA", new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("slowPeriod", "out")) });
        Wire("close", "fast", "in");
        Wire("close", "slow", "in");
        Node("up", "CrossesAbove");
        Node("down", "CrossesBelow");

        foreach (string direction in new[] { "up", "down" })
        {
            Wire("fast", direction, "a");
            Wire("slow", direction, "b");
        }

        Node("balance", "Balance");
        Node("flat", "PositionFlat");
        Node("noActive", "NoActiveOrders");
        Node("positiveBase", "HasBaseBalance", new Dictionary<string, ParamValue> { ["minQuantity"] = minQuantity });
        Binary("buyCrossFlat", "And", "up", "flat");
        Binary("buyEnabled", "And", "buyCrossFlat", "noActive");
        Binary("sellCrossReady", "And", "down", "positiveBase");
        Binary("sellEnabled", "And", "sellCrossReady", "noActive");
        Node(
            "buy",
            "MarketBuy",
            new Dictionary<string, ParamValue>
            {
                ["tag"] = EngineGraphFixture.Text("sma.entry"),
                ["amount"] = new WiredParamValue(new PortReference("buyAmount", "out"))
            },
            mode: ActionEvaluationMode.OnCandleClose);
        Node("sell", "MarketSell", new Dictionary<string, ParamValue> { ["tag"] = EngineGraphFixture.Text("sma.exit") }, mode: ActionEvaluationMode.OnCandleClose);
        Wire("buyEnabled", "buy", "enabled");
        Wire("sellEnabled", "sell", "enabled");
        Wire("balance.baseAvailable", "sell", "quantity");

        return new StrategyGraph(nodes, wires, StrategyTemplateKind.SmaCrossover);

        void Node(string id, string type, Dictionary<string, ParamValue>? parameters = null, ValueType? declared = null, ActionEvaluationMode? mode = null)
        {
            int schema = BuiltInComposites.Definitions.FirstOrDefault(definition => definition.TypeKey == type)?.SchemaVersion ??
                (type is "Parameter" or "Constant" or "CandleField" or "SMA" or "CrossesAbove" or "CrossesBelow" or "Compare" ? 2 : 1);
            nodes.Add(new GraphNode(id, type, schema, parameters ?? new Dictionary<string, ParamValue>(), declared, mode));
        }

        void Parameter(string id, ValueType type, decimal value)
        {
            Node(id, "Parameter", new Dictionary<string, ParamValue> { ["name"] = EngineGraphFixture.Text(id), ["value"] = EngineGraphFixture.Number(value) }, type);
        }

        void Wire(string source, string target, string input)
        {
            string[] pieces = source.Split('.');
            wires.Add(new GraphConnection(new PortReference(pieces[0], pieces.Length == 1 ? "out" : pieces[1]), new PortReference(target, input)));
        }

        void Binary(string id, string type, string a, string b)
        {
            Node(id, type);
            Wire(a, id, "a");
            Wire(b, id, "b");
        }
    }

    private static CompiledTradingGraph Compile(StrategyGraph graph)
    {
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, Definitions);
        result.Validation.Diagnostics.ShouldBeEmpty();

        return result.Graph.ShouldNotBeNull();
    }

    private static BacktestResult Run(CompiledTradingGraph graph, EngineExecutionInputs selections)
    {
        decimal[] prices = [4m, 3m, 2m, 5m, 6m, 2m, 1m];
        var input = new BacktestInput(
            graph,
            selections,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            prices.Select((price, index) => EngineGraphFixture.Candle(index, price)));

        return new BacktestEngine().Run(input);
    }
}