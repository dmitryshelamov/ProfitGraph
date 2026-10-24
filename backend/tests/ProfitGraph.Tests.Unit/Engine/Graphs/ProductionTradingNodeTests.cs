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

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class ProductionTradingNodeTests
{
    internal static IReadOnlyList<INodeDefinition> Catalog { get; } =
        Definitions.Concat(Enum.GetValues<TradingNodeBehavior>().Select(behavior => new TradingNodeDefinition(behavior))).ToArray();

    internal static GraphNode TradingNode(string id, string type, Dictionary<string, ParamValue>? parameters = null, ActionEvaluationMode? mode = null)
    {
        return new GraphNode(id, type, schemaVersion: 1, parameters ?? new Dictionary<string, ParamValue>(), evaluationMode: mode);
    }

    [TestCase("Fill.Price", ValueType.Price)]
    [TestCase("Fill.Side", ValueType.OrderSide)]
    [TestCase("Fill.Type", ValueType.OrderType)]
    [TestCase("Fill.FeeBase", ValueType.BaseAmount)]
    [TestCase("Fill.FeeQuote", ValueType.QuoteAmount)]
    public void SelectedFieldKeepsConcreteTypeThroughForwardedEvents(string field, ValueType expected)
    {
        var graph = new StrategyGraph(
            [
                TradingNode("fills", "OnOrderFilled"), TradingNode("fields", "EventFields", new Dictionary<string, ParamValue> { ["field"] = Text(field) }),
                Node("merge", "MergeEvents"), TradingNode("other", "OnOrderFilled")
            ],
            [Wire("fills", "merge", "a"), Wire("other", "merge", "b"), Wire("merge", "fields", "event")]);
        new GraphValidator().ValidateStrict(graph, Catalog).Diagnostics.ShouldBeEmpty();
        new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull().Nodes.Single(node => node.Node.Id == "fields").OutputTypes["out"].ShouldBe(expected);
    }

    [TestCase("Order.Price")]
    [TestCase("Candle.Close")]
    [TestCase("Unknown.Price")]
    public void WrongOrUnknownPayloadFieldIsRejectedBeforeHistory(string field)
    {
        var graph = new StrategyGraph(
            [TradingNode("fills", "OnOrderFilled"), TradingNode("fields", "EventFields", new Dictionary<string, ParamValue> { ["field"] = Text(field) })],
            [Wire("fills", "fields", "event")]);
        new GraphValidator().ValidateStrict(graph, Catalog).IsValid.ShouldBeFalse();
        new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldBeNull();
    }

    [Test]
    public void MixedPayloadMergeCannotBypassFieldValidation()
    {
        var graph = new StrategyGraph(
            [
                TradingNode("fills", "OnOrderFilled"), Node("close", "OnCandleClosed"), Node("merge", "MergeEvents"),
                TradingNode("fields", "EventFields")
            ],
            [Wire("fills", "merge", "a"), Wire("close", "merge", "b"), Wire("merge", "fields", "event")]);
        new GraphValidator().ValidateStrict(graph, Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleEventPayload);
        new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldBeNull();
    }

    [TestCase("Buy")]
    [TestCase("Sell")]
    public void ListenerFiltersUseClosedChoices(string side)
    {
        var graph = new StrategyGraph([TradingNode("listener", "OnOrderFilled", new Dictionary<string, ParamValue> { ["side"] = Text(side) })], []);
        new GraphValidator().ValidateStrict(graph, Catalog).IsValid.ShouldBeTrue();
        var invalid = new StrategyGraph([TradingNode("listener", "OnOrderFilled", new Dictionary<string, ParamValue> { ["side"] = Text("surprise") })], []);
        new GraphValidator().ValidateStrict(invalid, Catalog).IsValid.ShouldBeFalse();
    }

    [Test]
    public void MarketSubmissionAndPreviewUseDifferentPriceKnowledgeWithoutMutation()
    {
        var rules = new InstrumentRules(quantityStep: 1m, priceTick: 0.01m, minQuantity: 1m, minNotional: 1m, basePrecision: 6, quotePrecision: 2);
        var account = new TradingAccount(new ExecutionSettings(startingCapital: 100m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m), rules);
        AccountSnapshot snapshot = AccountSnapshot.Capture(account);
        var request = new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 10.999m, LimitPrice: null, "entry", "buy", EligibleFromCandleIndex: 1);
        OrderSizingResult preview = new OrderSizingService().Resolve(request, rules, account.Settings, snapshot, referencePrice: 100m);
        preview.Reason.ShouldBe(RejectionReason.ZeroQuantity);
        account.Portfolio.QuoteReserved.ShouldBe(0m);
        Order order = account.Submit(request).Order.ShouldNotBeNull();
        order.Reservation.ShouldBe(10.99m);
        order.Quantity.ShouldBeNull();
        snapshot.Orders.ShouldBeEmpty();
        snapshot.QuoteAvailable.ShouldBe(100m);
        account.Execute(order.Id, referencePrice: 10m).Fill.ShouldNotBeNull();
        account.Portfolio.LastBuyPrice.ShouldBe(10m);
    }

    [TestCase(arg1: null, arg2: null)]
    [TestCase("=", arg2: true)]
    [TestCase("!=", arg2: false)]
    [TestCase("<", arg2: null)]
    [TestCase(">", arg2: null)]
    public void ConcreteEnumComparisonSupportsOnlySameTypeEquality(string? operation, bool? expected)
    {
        var graph = new StrategyGraph(
            [
                TradingNode("a", "ActiveOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }),
                TradingNode("b", "ActiveOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }),
                Node("compare", "Compare", operation is null ? new Dictionary<string, ParamValue>() : new Dictionary<string, ParamValue> { ["op"] = Text(operation) })
            ],
            [
                new GraphConnection(new PortReference("a", "side"), new PortReference("compare", "a")),
                new GraphConnection(new PortReference("b", "side"), new PortReference("compare", "b"))
            ]);
        GraphValidationResult validation = new GraphValidator().ValidateStrict(graph, Catalog);
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, Catalog);

        if (!expected.HasValue)
        {
            validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits);
            result.Graph.ShouldBeNull();
            result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits);

            return;
        }

        CompiledTradingGraph compiled = result.Graph.ShouldNotBeNull();
        var account = new TradingAccount(
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 2, quotePrecision: 2));
        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 100m, LimitPrice: 90m, "entry", "origin", EligibleFromCandleIndex: 0))
            .IsAccepted.ShouldBeTrue();
        var market = new ReachedMarketContext(
            Candle(index: 0, close: 100m),
            candleIndex: 0,
            DateTimeOffset.UnixEpoch.AddHours(1),
            PricePhase.Open,
            marketPrice: 100m,
            eligibleFromCandleIndex: 0);
        var context = new DispatchEvaluationContext(AccountSnapshot.Capture(account), account.Rules, account.Settings, market);
        IReadOnlyDictionary<PortReference, EngineValue> values = new CandleValueEvaluator(compiled).ReadDispatch(Event(market.Candle), dispatch: context);
        values[new PortReference("compare", "out")].Boolean.ShouldBe(expected.Value);
        values[new PortReference("a", "side")].Side.ShouldBe(OrderSide.Buy);
        values[new PortReference("a", "reservedQuote")].Number.ShouldBe(99.9m);
        values[new PortReference("a", "reservedBase")].IsAvailable.ShouldBeFalse();
    }

    [Test]
    public void ComparisonCapturesActualTrustedSchemaDefault()
    {
        var original = new CompareNodeDefinition();
        ParamSpec[] parameters = [original.Params.Single() with { DefaultValue = "=" }];
        var definition = Substitute.For<INodeDefinition>();
        definition.TypeKey.Returns(original.TypeKey);
        definition.SchemaVersion.Returns(original.SchemaVersion);
        definition.Inputs.Returns(original.Inputs);
        definition.Outputs.Returns(original.Outputs);
        definition.Params.Returns(parameters);
        definition.IsPureComputation.Returns(true);
        definition.ResolveClock(Arg.Any<NodeClock>()).Returns(call => call.Arg<NodeClock>());
        definition.ResolveParameterType(Arg.Any<GraphNode>(), Arg.Any<ParamSpec>()).Returns(ValueType.Tag);
        definition.EvaluateEngine(Arg.Any<NodeEngineEvaluationContext>()).Returns(call => original.EvaluateEngine(call.Arg<NodeEngineEvaluationContext>()));
        definition.ResolveOutputType(Arg.Any<GraphNode>(), Arg.Any<Func<string, ValueType?>>(), Arg.Any<List<GraphDiagnostic>>())
            .Returns(call => original.ResolveOutputType(
                new GraphNode(
                    call.ArgAt<GraphNode>(0).Id,
                    original.TypeKey,
                    original.SchemaVersion,
                    new Dictionary<string, ParamValue> { ["op"] = new FixedParamValue(new TextParamLiteral("=")) }),
                call.ArgAt<Func<string, ValueType?>>(1),
                call.ArgAt<List<GraphDiagnostic>>(2)));
        INodeDefinition[] catalog = Catalog.Where(item => item.TypeKey != "Compare").Append(definition).ToArray();
        var graph = new StrategyGraph(
            [
                TradingNode("a", "ActiveOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }),
                TradingNode("b", "ActiveOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }), Node("compare", "Compare")
            ],
            [
                new GraphConnection(new PortReference("a", "side"), new PortReference("compare", "a")),
                new GraphConnection(new PortReference("b", "side"), new PortReference("compare", "b"))
            ]);
        new GraphValidator().ValidateStrict(graph, catalog).Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, catalog).Graph.ShouldNotBeNull();
        parameters[0] = original.Params.Single();
        var account = new TradingAccount(
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 2, quotePrecision: 2));
        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 100m, LimitPrice: null, "entry", "origin", EligibleFromCandleIndex: 0))
            .IsAccepted.ShouldBeTrue();
        var market = new ReachedMarketContext(
            Candle(index: 0, close: 100m),
            candleIndex: 0,
            DateTimeOffset.UnixEpoch.AddHours(1),
            PricePhase.Open,
            marketPrice: 100m,
            eligibleFromCandleIndex: 0);
        var context = new DispatchEvaluationContext(AccountSnapshot.Capture(account), account.Rules, account.Settings, market);
        new CandleValueEvaluator(compiled).ReadDispatch(Event(market.Candle), dispatch: context)[new PortReference("compare", "out")].Boolean.ShouldBeTrue();
        compiled.Nodes.Single(node => node.Node.Id == "compare").ParameterSpecs.Single().DefaultValue.ShouldBe("=");
    }

    [Test]
    public void MixedEnumTypesAndEnumArithmeticAreRejected()
    {
        foreach (string type in new[] { "Compare", "Arithmetic" })
        {
            var graph = new StrategyGraph(
                [
                    TradingNode("order", "ActiveOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }),
                    Node("operation", type, new Dictionary<string, ParamValue> { ["op"] = Text(type == "Compare" ? "=" : "Add") })
                ],
                [
                    new GraphConnection(new PortReference("order", "side"), new PortReference("operation", "a")),
                    new GraphConnection(new PortReference("order", "type"), new PortReference("operation", "b"))
                ]);
            new GraphValidator().ValidateStrict(graph, Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits);
        }
    }

    [Test]
    public void RejectedAttemptIdentityCannotTargetAnAcceptedOrder()
    {
        var graph = new StrategyGraph(
            [
                TradingNode("reject", "OnOrderRejected"),
                TradingNode("field", "EventFields", new Dictionary<string, ParamValue> { ["field"] = Text("Rejection.AttemptId") }),
                TradingNode("cancel", "CancelOrder", mode: ActionEvaluationMode.OnEvent)
            ],
            [Wire("reject", "field", "event"), Wire("reject", "cancel", "trigger"), Wire("field", "cancel", "orderId")]);
        new GraphValidator().ValidateStrict(graph, Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits);
        new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldBeNull();
    }

    [Test]
    public void FeeConsumesGrossBaseWithoutAverageCostAndLastBuyPriceIsActualSlippedFill()
    {
        var account = new TradingAccount(
            new ExecutionSettings(startingCapital: 1m, slippage: 0.01m, limitFeeRate: 0m, marketFeeRate: 0.0001m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 6, quotePrecision: 8));
        Order order = account
            .Submit(new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 0.000101m, LimitPrice: null, "entry", "origin", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        Fill fill = account.Execute(order.Id, referencePrice: 100m).Fill.ShouldNotBeNull();
        fill.Price.ShouldBe(101m);
        fill.BaseChange.ShouldBe(0m);
        CompiledTradingGraph graph = new TradingGraphCompiler().Compile(new StrategyGraph([TradingNode("position", "Position")], []), Catalog).Graph.ShouldNotBeNull();
        var market = new ReachedMarketContext(
            Candle(index: 0, close: 100m),
            candleIndex: 0,
            DateTimeOffset.UnixEpoch.AddHours(1),
            PricePhase.Open,
            marketPrice: 100m,
            eligibleFromCandleIndex: 0);
        IReadOnlyDictionary<PortReference, EngineValue> values = new CandleValueEvaluator(graph).ReadDispatch(
            Event(market.Candle),
            dispatch: new DispatchEvaluationContext(AccountSnapshot.Capture(account), account.Rules, account.Settings, market));
        values[new PortReference("position", "quantity")].Number.ShouldBe(0m);
        values[new PortReference("position", "averageCost")].IsAvailable.ShouldBeFalse();
        values[new PortReference("position", "lastBuyPrice")].Number.ShouldBe(101m);
        account.Portfolio.UnrealizedPnl(100m).ShouldBe(-0.000101m);
        (account.Portfolio.Equity(100m) - account.Portfolio.StartingCapital).ShouldBe(account.Portfolio.RealizedPnl + account.Portfolio.UnrealizedPnl(100m));
    }

    [TestCase(ActionEvaluationMode.OnEvent, arg2: true)]
    [TestCase(ActionEvaluationMode.OnCandleClose, arg2: false)]
    public void CancellationPublishesAndEnforcesItsSupportedModes(ActionEvaluationMode mode, bool valid)
    {
        var graph = new StrategyGraph(
            [Node("start", "OnStart"), TradingNode("cancel", "CancelOrder", new Dictionary<string, ParamValue> { ["tag"] = Text("entry") }, mode)],
            mode == ActionEvaluationMode.OnEvent ? [Wire("start", "cancel", "trigger")] : []);
        Catalog.Single(definition => definition.TypeKey == "CancelOrder").AllowedEvaluationModes.ShouldBe(new[] { ActionEvaluationMode.OnEvent });
        new GraphValidator().ValidateStrict(graph, Catalog).IsValid.ShouldBe(valid);
        (new TradingGraphCompiler().Compile(graph, Catalog).Graph is not null).ShouldBe(valid);
    }
}