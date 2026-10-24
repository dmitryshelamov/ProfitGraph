using NSubstitute;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class TradingGraphCompilerTests
{
    [TestCase("Sma")]
    [TestCase("Ladder")]
    public void OutputSlotsAreDenseAndStableAcrossGraphInputOrder(string workload)
    {
        StrategyGraph graph = workload == "Sma" ? SmaStrategyGraphGenerator.Generate() : LadderStrategyGraphGenerator.Generate();
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, ProductionTradingNodeTests.Catalog).Graph.ShouldNotBeNull();
        var reordered = new StrategyGraph(graph.Nodes.Reverse().ToArray(), graph.Connections.Reverse().ToArray(), graph.TemplateKind);
        CompiledTradingGraph second = new TradingGraphCompiler().Compile(reordered, ProductionTradingNodeTests.Catalog).Graph.ShouldNotBeNull();
        compiled.Nodes.Select(node => node.DependencyRank).ShouldBe(Enumerable.Range(start: 0, compiled.Nodes.Count));
        second.Nodes.Select(node => node.DependencyRank).ShouldBe(Enumerable.Range(start: 0, second.Nodes.Count));
        var expectedSlot = 0;

        foreach (CompiledTradingNode node in compiled.Nodes)
        {
            foreach (string key in node.OutputTypes.Keys.Order(StringComparer.Ordinal))
            {
                var reference = new PortReference(node.Node.Id, key);
                compiled.SlotOf(reference).ShouldBe(expectedSlot);
                second.SlotOf(reference).ShouldBe(expectedSlot);
                node.OutputSlotsByKey[key].ShouldBe(expectedSlot);
                expectedSlot++;
            }

            for (var index = 0; index < node.OutputBindings.Length; index++)
            {
                node.OutputSlots[index].Key.ShouldBe(compiled.SlotOf(node.OutputBindings[index].Key));
                node.OutputSlots[index].Value.ShouldBe(node.OutputBindings[index].Value);
            }

            foreach (CompiledInputBinding input in node.Connections)
            {
                input.SourceSlot.ShouldBe(compiled.SlotOf(input.Source));
            }

            foreach (CompiledEngineInput input in node.EngineInputs)
            {
                input.SourceSlot.ShouldBe(input.Source is null ? -1 : compiled.SlotOf(input.Source));
            }

            foreach ((string key, int slot) in node.WiredParameterSourceSlots)
            {
                slot.ShouldBe(compiled.SlotOf(((WiredParamValue)node.Node.Parameters[key]).Source));
            }
        }

        compiled.SlotCount.ShouldBe(expectedSlot);
        compiled.CachedOutputs.ShouldBe(
            compiled.Nodes.Where(node => node.Clock is NodeClock.Static or NodeClock.CandleClocked)
                .SelectMany(node => node.OutputSlots.Select(pair => pair.Key)));
    }

    [Test]
    public void WiredParametersAndInlineEngineSourcesShareTheOutputSlot()
    {
        var reference = new PortReference("source", "out");
        var source = new GraphNode(
            "source",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = Fixed(new DecimalParamLiteral(1m)) },
            ValueType.QuoteAmount);
        var wired = new GraphNode(
            "wired",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = new WiredParamValue(reference) },
            ValueType.QuoteAmount);
        GraphNode action = Node("action", "TestAction", new Dictionary<string, ParamValue> { ["amount"] = new WiredParamValue(reference) });
        CompiledTradingGraph compiled = Compile(new StrategyGraph([action, wired, source], []), Action()).Graph.ShouldNotBeNull();
        int slot = compiled.SlotOf(reference);

        compiled.Nodes.Single(node => node.Node.Id == "wired").WiredParameterSourceSlots["value"].ShouldBe(slot);
        CompiledTradingNode compiledAction = compiled.Nodes.Single(node => node.Node.Id == "action");
        compiledAction.EngineInputs.Single(input => input.Spec.Key == "amount").SourceSlot.ShouldBe(slot);
        compiledAction.EngineInputs.Single(input => input.Spec.Key == "enabled").SourceSlot.ShouldBe(-1);
    }

    [Test]
    public void WiredTextParameterIsRejectedBeforeCompilingSlots()
    {
        GraphNode source = EngineGraphFixture.Node("source", "Constant", new Dictionary<string, ParamValue> { ["value"] = EngineGraphFixture.Number(1m) });
        GraphNode arithmetic = EngineGraphFixture.Node(
            "arithmetic",
            "Arithmetic",
            new Dictionary<string, ParamValue> { ["op"] = new WiredParamValue(new PortReference("source", "out")) });
        var graph = new StrategyGraph(
            [source, arithmetic],
            [EngineGraphFixture.Wire("source", "arithmetic", "a"), EngineGraphFixture.Wire("source", "arithmetic", "b")]);

        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, EngineGraphFixture.Definitions);

        result.Graph.ShouldBeNull();
        result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatiblePort &&
            diagnostic.NodeId == "arithmetic" && diagnostic.ParamKey == "op");
    }

    [Test]
    public void ExactInputDefaultsAndLiteralsProduceImmutableBindings()
    {
        INodeDefinition action = Action();
        GraphNode node = Node(
            "action",
            "TestAction",
            new Dictionary<string, ParamValue>
            {
                ["amount"] = Fixed(new DecimalParamLiteral(9007199254740993.123456789m)),
                ["tag"] = Fixed(new TextParamLiteral("grid.entry"))
            });
        var graph = new StrategyGraph([node], []);

        CompiledTradingGraph compiled = Compile(graph, action).Graph.ShouldNotBeNull();
        compiled.Nodes.Count.ShouldBe(1);
        compiled.Nodes[0].DependencyRank.ShouldBe(0);
        var amount = (FixedParamValue)compiled.Nodes[0].EngineInputs.Single(input => input.Spec.Key == "amount").Inline!;
        ((DecimalParamLiteral)amount.Value).Value.ShouldBe(9007199254740993.123456789m);
        compiled.Nodes[0].EngineInputs.Single(input => input.Spec.Key == "enabled").Spec.DefaultValue!.Value.Boolean.ShouldBeTrue();
        ((IList<CompiledTradingNode>)compiled.Nodes).IsReadOnly.ShouldBeTrue();
        ((IList<CompiledEngineInput>)compiled.Nodes[0].EngineInputs).IsReadOnly.ShouldBeTrue();
    }

    [Test]
    public void ConnectionOverridesInvalidInlineWiredSourceAndPreservesStableRank()
    {
        var source = new GraphNode(
            "money",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = Fixed(new DecimalParamLiteral(1m)) },
            ValueType.QuoteAmount);
        GraphNode action = Node(
            "action",
            "TestAction",
            new Dictionary<string, ParamValue>
            {
                ["amount"] = new WiredParamValue(new PortReference("missing", "invalid"))
            });
        var graph = new StrategyGraph([action, source], [Connect("money", "out", "action", "amount")]);

        CompiledTradingGraph result = Compile(graph, Action()).Graph.ShouldNotBeNull();

        result.Nodes.Select(node => node.Node.Id).ShouldBe(new[] { "money", "action" });
        CompiledEngineInput binding = result.Nodes[1].EngineInputs.Single(input => input.Spec.Key == "amount");
        binding.Source.ShouldBe(new PortReference("money", "out"));
        binding.Inline.ShouldBeNull();
        binding.SourceSlot.ShouldBe(result.SlotOf(new PortReference("money", "out")));
    }

    [Test]
    public void MissingTriggerAndConflictingCandleModeAreDiagnostics()
    {
        GraphNode missing = Node(
            "action",
            "TestAction",
            new Dictionary<string, ParamValue> { ["amount"] = Fixed(new DecimalParamLiteral(1m)) },
            ActionEvaluationMode.OnEvent);
        Compile(new StrategyGraph([missing], []), Action()).Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.MissingTrigger);
        var start = new GraphNode("start", "OnStart", schemaVersion: 1, new Dictionary<string, ParamValue>());
        GraphNode compact = Node("action", "TestAction", new Dictionary<string, ParamValue> { ["amount"] = Fixed(new DecimalParamLiteral(1m)) });
        var graph = new StrategyGraph([start, compact], [Connect("start", "event", "action", "trigger")]);

        Compile(graph, Action(), new OnStartNodeDefinition())
            .Validation.Diagnostics
            .ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.TriggerWithCandleClockMode);
    }

    [Test]
    public void MissingAmountAndWrongTypedLiteralsAreRejected()
    {
        Compile(new StrategyGraph([Node("action", "TestAction", new Dictionary<string, ParamValue>())], []), Action())
            .Validation.Diagnostics
            .ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.MissingRequiredValueSource);

        foreach (ParamLiteral literal in new ParamLiteral[] { new BooleanParamLiteral(true), new TextParamLiteral("12"), new OrderIdParamLiteral(1) })
        {
            var graph = new StrategyGraph([Node("action", "TestAction", new Dictionary<string, ParamValue> { ["amount"] = Fixed(literal) })], []);
            Compile(graph, Action()).Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.InvalidLiteralRange);
        }
    }

    [TestCase("filled", arg2: true)]
    [TestCase("cancelled", arg2: true)]
    [TestCase("submitted", arg2: false)]
    [TestCase("rejected", arg2: false)]
    [TestCase("completed", arg2: false)]
    public void OnlyFilledAndCancelledCutLifecycleFeedback(string output, bool accepted)
    {
        GraphNode node = Node(
            "action",
            "TestAction",
            new Dictionary<string, ParamValue> { ["amount"] = Fixed(new DecimalParamLiteral(1m)) },
            ActionEvaluationMode.OnEvent);
        var graph = new StrategyGraph([node], [Connect("action", output, "action", "trigger")]);

        TradingGraphCompilationResult result = Compile(graph, Action());

        result.Validation.IsValid.ShouldBe(accepted);

        if (accepted)
        {
            result.Graph.ShouldNotBeNull().DeferredLifecycleRoutes.ShouldBe(graph.Connections);
        }
        else
        {
            result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.ValueOrImmediateActionCycle);
        }
    }

    [TestCase(ValueType.Boolean)]
    [TestCase(ValueType.Tag)]
    [TestCase(ValueType.OrderId)]
    public void TypedConstantLiteralMeaningsAreNotNumericConversions(ValueType type)
    {
        ParamLiteral literal = type switch
        {
            ValueType.Boolean => new BooleanParamLiteral(true),
            ValueType.Tag => new TextParamLiteral("grid.entry"),
            _ => new OrderIdParamLiteral(long.MaxValue)
        };
        var node = new GraphNode("source", "Constant", schemaVersion: 2, new Dictionary<string, ParamValue> { ["value"] = Fixed(literal) }, type);

        CompiledTradingGraph result = Compile(new StrategyGraph([node], [])).Graph.ShouldNotBeNull();

        result.Nodes[0].OutputTypes["out"].ShouldBe(type);
    }

    [Test]
    public void OrdinaryValueCycleIsRejected()
    {
        var constant = new GraphNode("value", "Constant", schemaVersion: 2, new Dictionary<string, ParamValue>());
        var add = new GraphNode("add", "Arithmetic", schemaVersion: 2, new Dictionary<string, ParamValue>());
        var graph = new StrategyGraph([constant, add], [Connect("add", "out", "add", "a"), Connect("value", "out", "add", "b")]);

        Compile(graph, new ArithmeticNodeDefinition())
            .Validation.Diagnostics
            .ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.ValueOrImmediateActionCycle);
    }

    [Test]
    public void CompileFreezesSourceParametersAndDefinitionInputCollection()
    {
        INodeDefinition action = Action();
        List<EngineInputSpec> specs = action.EngineInputs.ToList();
        action.EngineInputs.Returns(specs);
        var values = new Dictionary<string, ParamValue> { ["amount"] = Fixed(new DecimalParamLiteral(1m)) };
        var nodes = new List<GraphNode> { Node("action", "TestAction", values) };
        var graph = new StrategyGraph(nodes, []);

        CompiledTradingGraph compiled = Compile(graph, action).Graph.ShouldNotBeNull();
        values.Clear();
        specs.Clear();
        nodes.Clear();

        compiled.Nodes.Count.ShouldBe(1);
        compiled.Nodes[0].EngineInputs.Count.ShouldBe(3);
        ((DecimalParamLiteral)((FixedParamValue)compiled.Nodes[0].EngineInputs.Single(input => input.Spec.Key == "amount").Inline!).Value).Value.ShouldBe(1m);
    }

    [Test]
    public void ExactPriceCannotSupplyQuoteAmountEvenThroughWiredInline()
    {
        var source = new GraphNode(
            "price",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = Fixed(new DecimalParamLiteral(1m)) },
            ValueType.Price);
        GraphNode action = Node("action", "TestAction", new Dictionary<string, ParamValue> { ["amount"] = new WiredParamValue(new PortReference("price", "out")) });

        Compile(new StrategyGraph([source, action], []), Action())
            .Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits);
    }

    [Test]
    public void DecimalOptimizationRangeIsPreservedAndInvalidRangesAreDiagnosed()
    {
        var valid = new DecimalOptimizeParamValue(Min: 1.0000000000000000000000000001m, Max: 2m, Step: 0.0000000000000000000000000001m);
        var graph = new StrategyGraph([Node("action", "TestAction", new Dictionary<string, ParamValue> { ["amount"] = valid })], []);
        Compile(graph, Action()).Graph.ShouldNotBeNull().Nodes[0].EngineInputs.Single(input => input.Spec.Key == "amount").Inline.ShouldBe(valid);

        foreach (DecimalOptimizeParamValue invalid in new[] { valid with { Min = 3m }, valid with { Step = 0m }, valid with { Step = -1m } })
        {
            var invalidGraph = new StrategyGraph([Node("action", "TestAction", new Dictionary<string, ParamValue> { ["amount"] = invalid })], []);
            Compile(invalidGraph, Action()).Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.InvalidLiteralRange);
        }
    }

    [TestCase("Constant")]
    [TestCase("Parameter")]
    public void DeclaredBooleanSourceConnectsToEnabledAndBooleanOperator(string type)
    {
        var source = new GraphNode(
            "condition",
            type,
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = Fixed(new BooleanParamLiteral(true)) },
            ValueType.Boolean);
        GraphNode action = Node(
            "action",
            "TestAction",
            new Dictionary<string, ParamValue>
            {
                ["amount"] = Fixed(new DecimalParamLiteral(1m)), ["enabled"] = Fixed(new BooleanParamLiteral(false))
            });
        var and = new GraphNode("and", "And", schemaVersion: 1, new Dictionary<string, ParamValue>());
        var graph = new StrategyGraph(
            [source, action, and],
            [Connect("condition", "out", "action", "enabled"), Connect("condition", "out", "and", "a"), Connect("condition", "out", "and", "b")]);

        INodeDefinition[] definitions = [Action(), new ParameterNodeDefinition(), new AndNodeDefinition(), new ConstantNodeDefinition()];
        new GraphValidator().Validate(graph, definitions).IsValid.ShouldBeTrue();
        new GraphValidator().ValidateStrict(graph, definitions).IsValid.ShouldBeTrue();
        CompiledTradingGraph result = new TradingGraphCompiler().Compile(graph, definitions).Graph.ShouldNotBeNull();

        CompiledEngineInput enabled = result.Nodes.Single(node => node.Node.Id == "action").EngineInputs.Single(input => input.Spec.Key == "enabled");
        enabled.Source.ShouldBe(new PortReference("condition", "out"));
        enabled.Inline.ShouldBeNull();
        result.Nodes.Single(node => node.Node.Id == "condition").OutputTypes["out"].ShouldBe(ValueType.Boolean);
        new ConstantNodeDefinition().Outputs[0].Kind.ShouldBe(PortKind.Series);
        new ParameterNodeDefinition().Outputs[0].Kind.ShouldBe(PortKind.Series);
    }

    [Test]
    public void NumericSeriesCannotConnectToCondition()
    {
        var source = new GraphNode("number", "Constant", schemaVersion: 2, new Dictionary<string, ParamValue> { ["value"] = Fixed(new DecimalParamLiteral(1m)) });
        GraphNode action = Node("action", "TestAction", new Dictionary<string, ParamValue> { ["amount"] = Fixed(new DecimalParamLiteral(1m)) });
        var graph = new StrategyGraph([source, action], [Connect("number", "out", "action", "enabled")]);

        INodeDefinition[] definitions = [Action(), new ConstantNodeDefinition()];
        new GraphValidator().ValidateStrict(graph, definitions).Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatiblePort);
        new TradingGraphCompiler().Compile(graph, definitions)
            .Validation.Diagnostics
            .ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatiblePort);
    }

    [TestCase(null)]
    [TestCase((ActionEvaluationMode)999)]
    public void ConnectedTriggerCannotSupplyMissingOrUndefinedEvaluationMode(ActionEvaluationMode? mode)
    {
        var action = new GraphNode(
            "action",
            "TestAction",
            schemaVersion: 1,
            new Dictionary<string, ParamValue> { ["amount"] = Fixed(new DecimalParamLiteral(1m)) },
            evaluationMode: mode);
        var start = new GraphNode("start", "OnStart", schemaVersion: 1, new Dictionary<string, ParamValue>());
        var graph = new StrategyGraph([start, action], [Connect("start", "event", "action", "trigger")]);
        INodeDefinition[] definitions = [Action(), new OnStartNodeDefinition()];

        new GraphValidator().Validate(graph, definitions)
            .Diagnostics
            .ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.InvalidNodeMetadata && diagnostic.NodeId == "action");
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, definitions);

        result.Graph.ShouldBeNull();
        result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.InvalidNodeMetadata);
    }

    [TestCase("SMA", ValueType.Tag)]
    [TestCase("SMA", ValueType.OrderId)]
    [TestCase("SMA", ValueType.Boolean)]
    [TestCase("SMA", ValueType.Candle)]
    [TestCase("SMA", ValueType.Order)]
    [TestCase("SMA", ValueType.Fill)]
    [TestCase("SMA", ValueType.Event)]
    [TestCase("PreviousCandleValue", ValueType.Tag)]
    [TestCase("PreviousCandleValue", ValueType.OrderId)]
    [TestCase("PreviousCandleValue", ValueType.Boolean)]
    [TestCase("PreviousCandleValue", ValueType.Candle)]
    [TestCase("PreviousCandleValue", ValueType.Order)]
    [TestCase("PreviousCandleValue", ValueType.Fill)]
    [TestCase("PreviousCandleValue", ValueType.Event)]
    public void NumericStatefulIndicatorsRejectNonnumericSeries(string type, ValueType valueType)
    {
        var source = Substitute.For<INodeDefinition>();
        source.TypeKey.Returns("TypedSource");
        source.SchemaVersion.Returns(1);
        source.Inputs.Returns(Array.Empty<PortSpec>());
        source.Outputs.Returns(new[] { new PortSpec("out", PortKind.Series, "Value", valueType) });
        source.Params.Returns(Array.Empty<ParamSpec>());
        source.EngineInputs.Returns(Array.Empty<EngineInputSpec>());
        var graph = new StrategyGraph(
            [
                new GraphNode("source", "TypedSource", schemaVersion: 1, new Dictionary<string, ParamValue>()),
                new GraphNode("indicator", type, schemaVersion: 2, new Dictionary<string, ParamValue>())
            ],
            [Connect("source", "out", "indicator", "in")]);
        INodeDefinition indicator = type == "SMA" ? new SmaNodeDefinition() : new PreviousCandleValueNodeDefinition();
        INodeDefinition[] definitions = [source, indicator];

        GraphValidationResult validation = new GraphValidator().ValidateStrict(graph, definitions);

        validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits &&
            diagnostic.NodeId == "indicator" && diagnostic.PortKey == "in");
        new TradingGraphCompiler().Compile(graph, definitions).Graph.ShouldBeNull();
    }

    private static TradingGraphCompilationResult Compile(StrategyGraph graph, params INodeDefinition[] definitions)
    {
        return new TradingGraphCompiler().Compile(graph, definitions.Append(new ConstantNodeDefinition()).ToArray());
    }

    private static INodeDefinition Action()
    {
        var definition = Substitute.For<INodeDefinition>();
        definition.TypeKey.Returns("TestAction");
        definition.SchemaVersion.Returns(1);
        definition.IsAction.Returns(true);
        definition.Inputs.Returns(
            new[]
            {
                new PortSpec("trigger", PortKind.Event, "Trigger", ValueType.Event),
                new PortSpec("amount", PortKind.Series, "Amount", ValueType.QuoteAmount), new PortSpec("enabled", PortKind.Condition, "Enabled", ValueType.Boolean),
                new PortSpec("tag", PortKind.Series, "Tag", ValueType.Tag)
            });
        definition.Outputs.Returns(
            new[] { "filled", "cancelled", "submitted", "rejected", "completed" }
                .Select(key => new PortSpec(key, PortKind.Event, key, ValueType.Event))
                .ToArray());
        definition.Params.Returns(Array.Empty<ParamSpec>());
        definition.EngineInputs.Returns(
            new[]
            {
                new EngineInputSpec("amount", ValueType.QuoteAmount, required: true, supportsInline: true),
                new EngineInputSpec("enabled", ValueType.Boolean, required: false, supportsInline: true, EngineValue.FromBoolean(true)),
                new EngineInputSpec("tag", ValueType.Tag, required: false, supportsInline: true)
            });

        return definition;
    }

    private static GraphNode Node(string id, string type, IReadOnlyDictionary<string, ParamValue> values, ActionEvaluationMode mode = ActionEvaluationMode.OnCandleClose)
    {
        return new GraphNode(id, type, schemaVersion: 1, values, evaluationMode: mode);
    }

    private static FixedParamValue Fixed(ParamLiteral literal)
    {
        return new FixedParamValue(literal);
    }

    private static GraphConnection Connect(string source, string output, string target, string input)
    {
        return new GraphConnection(new PortReference(source, output), new PortReference(target, input));
    }
}