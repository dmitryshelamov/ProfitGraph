using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class EventFieldDefaultTests
{
    [TestCase("decimal")]
    [TestCase("boolean")]
    [TestCase("optimize")]
    [TestCase("wired")]
    public void MalformedFieldReturnsDiagnosticsAcrossValidationAndCompilation(string mode)
    {
        ParamValue setting = mode switch
        {
            "decimal" => Number(1m),
            "boolean" => new FixedParamValue(new BooleanParamLiteral(true)),
            "optimize" => new OptimizeParamValue(Min: 1d, Max: 2d, Step: 1d),
            "wired" => new WiredParamValue(new PortReference("source", "out")),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), Node("source", "Constant"),
                TradingNode("field", "EventFields", new Dictionary<string, ParamValue> { ["field"] = setting }), Node("consumer", "Not")
            ],
            [Wire("start", "field", "event"), Wire("field", "consumer")]);
        var validator = new GraphValidator();
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, Catalog);
        compilation.Graph.ShouldBeNull();
        GraphValidationResult[] results = [validator.Validate(graph, Catalog), validator.ValidateStrict(graph, Catalog), compilation.Validation];
        GraphDiagnosticCode expected = mode == "wired" ? GraphDiagnosticCode.IncompatiblePort : GraphDiagnosticCode.InvalidParameter;

        foreach (GraphValidationResult result in results)
        {
            result.IsValid.ShouldBeFalse();
            result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == expected && diagnostic.NodeId == "field" && diagnostic.ParamKey == "field");
        }
    }

    [TestCase("CancelResult.Cancelled", ValueType.Boolean, arg3: false)]
    [TestCase("CancelResult.Cancelled", ValueType.Boolean, arg3: true)]
    [TestCase("Order.Side", ValueType.OrderSide, arg3: false)]
    [TestCase("Order.Side", ValueType.OrderSide, arg3: true)]
    [TestCase("Order.ReservedQuote", ValueType.QuoteAmount, arg3: false)]
    [TestCase("Order.ReservedQuote", ValueType.QuoteAmount, arg3: true)]
    [TestCase("Order.Id", ValueType.OrderId, arg3: false)]
    [TestCase("Order.Id", ValueType.OrderId, arg3: true)]
    [TestCase("Fill.Price", ValueType.Price, arg3: false)]
    [TestCase("Fill.Price", ValueType.Price, arg3: true)]
    public void ActualDefaultAndExplicitFieldHaveSameCapturedDispatchBehavior(string field, ValueType expected, bool explicitField)
    {
        var original = new TradingNodeDefinition(TradingNodeBehavior.EventFields);
        ParamSpec[] parameters = [original.Params.Single() with { DefaultValue = field }];
        INodeDefinition definition = Definition(original, parameters);
        INodeDefinition[] catalog = Catalog.Where(item => item.TypeKey != "EventFields").Append(definition).ToArray();
        bool cancel = expected == ValueType.Boolean;
        var nodes = new List<GraphNode>
        {
            Node("start", "OnStart"),
            cancel
                ? TradingNode("action", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("absent") }, ActionEvaluationMode.OnEvent)
                : TradingNode(
                    "action",
                    "MarketBuy",
                    new Dictionary<string, ParamValue> { ["amount"] = Number(100m), ["tag"] = Text("entry") },
                    ActionEvaluationMode.OnEvent),
            TradingNode("field", "EventFields", explicitField ? new Dictionary<string, ParamValue> { ["field"] = Text(field) } : null)
        };
        string eventPort = cancel ? "completed" : expected == ValueType.Price ? "filled" : "submitted";
        var connections = new List<GraphConnection>
        {
            Wire("start", "action", "trigger"), new GraphConnection(new PortReference("action", eventPort), new PortReference("field", "event"))
        };

        if (cancel)
        {
            nodes.Add(Node("consumer", "Not"));
            connections.Add(Wire("field", "consumer"));
        }
        else if (expected == ValueType.OrderId)
        {
            nodes.Add(TradingNode("consumer", "CancelOrder", mode: ActionEvaluationMode.OnEvent));
            connections.Add(new GraphConnection(new PortReference("action", "submitted"), new PortReference("consumer", "trigger")));
            connections.Add(Wire("field", "consumer", "orderId"));
        }
        else
        {
            nodes.Add(Node("consumer", "Compare", new Dictionary<string, ParamValue> { ["op"] = Text("=") }));
            connections.Add(Wire("field", "consumer", "a"));
            connections.Add(Wire("field", "consumer", "b"));
        }

        var graph = new StrategyGraph(nodes, connections);
        new GraphValidator().ValidateStrict(graph, catalog).Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, catalog).Graph.ShouldNotBeNull();
        parameters[0] = original.Params.Single() with { DefaultValue = "Unknown.Field" };
        compiled.Nodes.Single(node => node.Node.Id == "field").EventField.ShouldNotBeNull().Key.ShouldBe(field);
        var account = new TradingAccount(
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 2, quotePrecision: 2));
        var dispatcher = new CausalDispatcher(compiled, account);
        var market = new ReachedMarketContext(
            Candle(index: 0, close: 100m),
            candleIndex: 0,
            DateTimeOffset.UnixEpoch.AddHours(1),
            PricePhase.Open,
            marketPrice: 100m,
            eligibleFromCandleIndex: 0);
        dispatcher.BeginOpen(market);
        dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), market);
        DispatchBatch batch = dispatcher.Drain();

        if (expected == ValueType.Price)
        {
            Order order = account.Orders.Active.Single();
            OrderSnapshot snapshot = OrderSnapshot.Capture(order);
            OrderExecution execution = account.Execute(order.Id, referencePrice: 100m);
            dispatcher.PublishExecution(snapshot, execution, market);
            batch = dispatcher.Drain();
        }

        DispatchObservation observation = batch.Observations.Single(item => item.Outputs[new PortReference("field", "out")].IsAvailable);
        EngineValue value = observation.Outputs[new PortReference("field", "out")];
        value.ValueType.ShouldBe(expected);

        switch (expected)
        {
            case ValueType.Boolean:
                value.Boolean.ShouldBeFalse();
                observation.Outputs[new PortReference("consumer", "out")].Boolean.ShouldBeTrue();

                break;
            case ValueType.OrderId:
                value.OrderId.ShouldBe(1L);
                account.Orders.Active.ShouldBeEmpty();
                batch.AppliedActions.ShouldContain(fact => fact.NodeId == "consumer");

                break;
            case ValueType.OrderSide:
                value.Side.ShouldBe(OrderSide.Buy);
                observation.Outputs[new PortReference("consumer", "out")].Boolean.ShouldBeTrue();

                break;
            default:
                value.Number.ShouldBe(100m);
                observation.Outputs[new PortReference("consumer", "out")].Boolean.ShouldBeTrue();

                break;
        }
    }

    [TestCase("Unknown.Field", arg2: false)]
    [TestCase("Unknown.Field", arg2: true)]
    [TestCase("Fill.Price", arg2: false)]
    [TestCase("Fill.Price", arg2: true)]
    public void ActualDefaultAndExplicitFieldRejectUnknownOrIncompatiblePayload(string field, bool explicitField)
    {
        var original = new TradingNodeDefinition(TradingNodeBehavior.EventFields);
        INodeDefinition definition = Definition(original, [original.Params.Single() with { DefaultValue = field }]);
        INodeDefinition[] catalog = Catalog.Where(item => item.TypeKey != "EventFields").Append(definition).ToArray();
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), TradingNode("field", "EventFields", explicitField ? new Dictionary<string, ParamValue> { ["field"] = Text(field) } : null)],
            [Wire("start", "field", "event")]);
        GraphDiagnosticCode expected = explicitField && field == "Unknown.Field" ? GraphDiagnosticCode.InvalidParameter : GraphDiagnosticCode.IncompatibleEventPayload;
        new GraphValidator().ValidateStrict(graph, catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.Code == expected);
        new TradingGraphCompiler().Compile(graph, catalog).Graph.ShouldBeNull();
    }

    private static INodeDefinition Definition(TradingNodeDefinition original, IReadOnlyList<ParamSpec> parameters)
    {
        var definition = Substitute.For<INodeDefinition>();
        definition.TypeKey.Returns(original.TypeKey);
        definition.SchemaVersion.Returns(original.SchemaVersion);
        definition.Inputs.Returns(original.Inputs);
        definition.Outputs.Returns(original.Outputs);
        definition.Params.Returns(parameters);
        definition.TradingBehavior.Returns(original.TradingBehavior);
        definition.EngineClock.Returns(original.EngineClock);
        definition.ResolveClock(Arg.Any<NodeClock>()).Returns(NodeClock.Event);
        definition.EvaluatesDispatch.Returns(true);
        definition.ResolveOutputType(Arg.Any<GraphNode>(), Arg.Any<Func<string, ValueType?>>(), Arg.Any<List<GraphDiagnostic>>())
            .Returns(call => EventFieldCatalog.Find(NodeTextSettingResolver.Resolve(call.ArgAt<GraphNode>(0), parameters, "field"))?.ValueType);
        definition.ResolveParameterType(Arg.Any<GraphNode>(), Arg.Any<ParamSpec>()).Returns(ValueType.Tag);
        definition.EvaluateEngine(Arg.Any<NodeEngineEvaluationContext>()).Returns(call => original.EvaluateEngine(call.Arg<NodeEngineEvaluationContext>()));
        definition.EventFields.Returns(original.EventFields);

        return definition;
    }
}