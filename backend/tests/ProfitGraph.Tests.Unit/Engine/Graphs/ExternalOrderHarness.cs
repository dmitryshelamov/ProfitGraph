using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

internal sealed class ExternalOrderHarness
{
    private readonly Candle _candle = Candle(index: 0, close: 5m);

    public ScriptedExternalAccount Account { get; }
    public CausalDispatcher Dispatcher { get; }

    public ExternalOrderHarness(bool twoBuys = false, bool cancelOnSubmitted = false, bool duplicateCancel = false)
        : this(CompileGraph(twoBuys, cancelOnSubmitted, duplicateCancel))
    {
    }

    public ExternalOrderHarness(CompiledTradingGraph graph)
    {
        var settings = new ExecutionSettings(startingCapital: 250m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var rules = new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 2, quotePrecision: 2);
        Account = new ScriptedExternalAccount(settings, rules);
        Dispatcher = new CausalDispatcher(graph, Account);
    }

    public DispatchBatch Start()
    {
        var context = new ReachedMarketContext(_candle, candleIndex: 0, _candle.OpenTime.AddHours(1), PricePhase.Open, _candle.Open, eligibleFromCandleIndex: 0);
        Dispatcher.BeginOpen(context);
        Dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), context);
        DispatchBatch batch = Dispatcher.Drain();
        Dispatcher.AdvanceClosedCandle(_candle, historyIndex: 0);

        return batch;
    }

    public DispatchBatch Update(ExternalOrderUpdate update, decimal price = 5m)
    {
        var context = new ReachedMarketContext(
            _candle,
            candleIndex: 0,
            _candle.OpenTime.AddHours(1),
            PricePhase.External,
            price,
            eligibleFromCandleIndex: 1);
        Dispatcher.ApplyExternalOrderUpdate(update, context);

        return Dispatcher.Drain();
    }

    private static CompiledTradingGraph CompileGraph(bool twoBuys, bool cancelOnSubmitted, bool duplicateCancel)
    {
        var nodes = new List<GraphNode>
        {
            Node("start", "OnStart"),
            TradingNode(
                "buy",
                "LimitBuy",
                new Dictionary<string, ParamValue> { ["amount"] = Number(25m), ["price"] = Number(5m), ["tag"] = Text("entry") },
                ActionEvaluationMode.OnEvent)
        };
        var connections = new List<GraphConnection> { Wire("start", "buy", "trigger") };

        if (twoBuys)
        {
            nodes.Add(
                TradingNode(
                    "buyAgain",
                    "LimitBuy",
                    new Dictionary<string, ParamValue> { ["amount"] = Number(25m), ["price"] = Number(5m), ["tag"] = Text("entry") },
                    ActionEvaluationMode.OnEvent));
            connections.Add(Wire("start", "buyAgain", "trigger"));
        }

        if (cancelOnSubmitted)
        {
            nodes.Add(TradingNode("cancel", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent));
            connections.Add(new GraphConnection(new PortReference("buy", "submitted"), new PortReference("cancel", "trigger")));
        }

        if (duplicateCancel)
        {
            nodes.Add(TradingNode("cancelAgain", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }, ActionEvaluationMode.OnEvent));
            connections.Add(new GraphConnection(new PortReference("buy", "submitted"), new PortReference("cancelAgain", "trigger")));
        }

        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(new StrategyGraph(nodes, connections), Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }
}