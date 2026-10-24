using NSubstitute;

using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Blocks;

public sealed class StrategyValueDagValidationTests
{
    [Test]
    public async Task FortySharedDoublingValuesValidateLowerAndCompileWithoutExponentialExpansion()
    {
        StrategyDefinition definition = Doubling(new ValueExpression("constant", ValueType.Scalar, number: 1m));
        StrategyBlockValidationResult result = await Task.Run(() => new StrategyDefinitionValidator().Validate(definition, Catalog)).WaitAsync(TimeSpan.FromSeconds(5));
        result.Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        lowered.Graph.Nodes.Count.ShouldBe(44);
        TradingGraphCompilationResult compiled = await Task.Run(() => new TradingGraphCompiler().Compile(lowered.Graph, Catalog)).WaitAsync(TimeSpan.FromSeconds(5));
        compiled.Validation.Diagnostics.ShouldBeEmpty();
        compiled.Graph.ShouldNotBeNull();
    }

    [Test]
    public async Task InvalidSharedFieldIsInspectedOnceAndReportedAtEveryConsumer()
    {
        var position = Substitute.For<INodeDefinition>();
        position.TypeKey.Returns("Position");
        position.Outputs.Returns([new PortSpec("quantity", PortKind.Series, "Количество", ValueType.BaseAmount)]);
        INodeDefinition[] definitions = Catalog.Where(definition => definition.TypeKey != "Position").Append(position).ToArray();
        StrategyDefinition definition = Doubling(new ValueExpression("position", field: "missing"));
        StrategyBlockValidationResult result =
            await Task.Run(() => new StrategyDefinitionValidator().Validate(definition, definitions)).WaitAsync(TimeSpan.FromSeconds(5));
        result.IsValid.ShouldBeFalse();
        _ = position.Received(1).Outputs;
        result.Diagnostics.Count.ShouldBe(42);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.ValueName == "v0" && diagnostic.Code == GraphDiagnosticCode.UnknownPort);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.ValueName == "v40" && diagnostic.Code == GraphDiagnosticCode.UnknownPort);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.BlockId == "check" && diagnostic.Code == GraphDiagnosticCode.UnknownPort);
    }

    [Test]
    public void CyclesRemainDiagnosticsForEachNamedValueAndReferencingBlock()
    {
        var definition = new StrategyDefinition(
            [],
            [new StrategyValue("first", Reference("second")), new StrategyValue("second", Reference("first"))],
            [new StrategyBlock("start", "event", source: "OnStart"), Check("check", Reference("second"))],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.Cycle && diagnostic.ValueName == "first");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.Cycle && diagnostic.ValueName == "second");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.Cycle && diagnostic.BlockId == "check");
    }

    [Test]
    public void CachedIndicatorErrorsRetainTheirParameterSource()
    {
        var indicator = new ValueExpression(
            "SMA",
            arguments: new Dictionary<string, ValueExpression>
            {
                ["in"] = new ValueExpression("candle", field: "Close"), ["period"] = new ValueExpression("parameter", name: "period")
            });
        var definition = new StrategyDefinition(
            [new StrategyParameter("period", ValueType.Integer, DefaultValue: 0m)],
            [new StrategyValue("sma", indicator), new StrategyValue("alias", Reference("sma"))],
            [new StrategyBlock("start", "event", source: "OnStart"), Check("check", Reference("alias"))],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.Diagnostics.ShouldNotBeEmpty();
        result.Diagnostics.ShouldAllBe(diagnostic => diagnostic.ParameterName == "period" && diagnostic.ValueName == null && diagnostic.BlockId == null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SharedErrorsInCyclesReachEveryConsumerRegardlessOfDeclarationOrder(bool reverse)
    {
        StrategyValue[] values =
        [
            new StrategyValue(
                "first",
                new ValueExpression(
                    "arithmetic",
                    operation: "Add",
                    arguments: new Dictionary<string, ValueExpression>
                    {
                        ["a"] = Reference("second"), ["b"] = Reference("invalid")
                    })),
            new StrategyValue("second", Reference("first")), new StrategyValue("invalid", new ValueExpression("position", field: "missing"))
        ];
        var definition = new StrategyDefinition(
            [],
            reverse ? values.Reverse() : values,
            [new StrategyBlock("start", "event", source: "OnStart"), Check("check", Reference("second"))],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnknownPort && diagnostic.ValueName == "first");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnknownPort && diagnostic.ValueName == "second");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnknownPort && diagnostic.BlockId == "check");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.Cycle && diagnostic.BlockId == "check");
    }

    private static StrategyDefinition Doubling(ValueExpression initial)
    {
        var values = new List<StrategyValue> { new StrategyValue("v0", initial) };

        for (var index = 1; index <= 40; index++)
        {
            values.Add(
                new StrategyValue(
                    "v" + index,
                    new ValueExpression(
                        "arithmetic",
                        operation: "Add",
                        arguments: new Dictionary<string, ValueExpression>
                        {
                            ["a"] = Reference("v" + (index - 1)), ["b"] = Reference("v" + (index - 1))
                        })));
        }

        return new StrategyDefinition(
            [],
            values,
            [new StrategyBlock("start", "event", source: "OnStart"), Check("check", Reference("v40"))],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
    }

    private static StrategyBlock Check(string id, ValueExpression expression)
    {
        return new StrategyBlock(
            id,
            "condition",
            join: "all",
            checks:
            [
                new StrategyCheck(
                    "Compare",
                    "=",
                    new Dictionary<string, ValueExpression>
                    {
                        ["a"] = expression, ["b"] = expression
                    })
            ]);
    }

    private static ValueExpression Reference(string name)
    {
        return new ValueExpression("value", name: name);
    }
}