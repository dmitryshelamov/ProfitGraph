using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Dependencies;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphValidatorTests
{
    [Test]
    public void EmptyGraph_IsInvalid()
    {
        AssertCode(new StrategyGraph([], []), GraphDiagnosticCode.EmptyGraph);
    }

    [TestCase("absent", arg2: 1, GraphDiagnosticCode.UnknownNodeType)]
    [TestCase("Constant", arg2: 99, GraphDiagnosticCode.UnsupportedSchemaVersion)]
    public void UnknownTypeOrSchema_ReturnsDiagnostic(string typeKey, int version, GraphDiagnosticCode code)
    {
        var node = new GraphNode("a", typeKey, version, new Dictionary<string, ParamValue>());
        AssertCode(new StrategyGraph([node], []), code);
    }

    [Test]
    public void DuplicateIds_AreRejectedWithoutThrowing()
    {
        AssertCode(new StrategyGraph([GraphNodeGenerator.Create("a"), GraphNodeGenerator.Create("a")], []), GraphDiagnosticCode.DuplicateNodeId);
    }

    [Test]
    public void MissingRequiredInput_IsInvalid()
    {
        AssertCode(new StrategyGraph([GraphNodeGenerator.Create("a", "SMA")], []), GraphDiagnosticCode.MissingInput);
    }

    [Test]
    public void PortKindsMustMatch()
    {
        var graph = new StrategyGraph(
            [GraphNodeGenerator.Create("a"), GraphNodeGenerator.Create("b", "Not")],
            [GraphNodeGenerator.Connect("a", "b")]);
        AssertCode(graph, GraphDiagnosticCode.IncompatiblePort);
    }

    [Test]
    public void InputCannotHaveTwoSources()
    {
        var graph = new StrategyGraph(
            [GraphNodeGenerator.Create("a"), GraphNodeGenerator.Create("b"), GraphNodeGenerator.Create("c", "SMA")],
            [GraphNodeGenerator.Connect("a", "c"), GraphNodeGenerator.Connect("b", "c")]);
        AssertCode(graph, GraphDiagnosticCode.MultipleInputConnections);
    }

    [TestCase("missing", "out", GraphDiagnosticCode.UnknownNode)]
    [TestCase("a", "missing", GraphDiagnosticCode.UnknownPort)]
    public void WiredSourceMustResolve(string sourceId, string portKey, GraphDiagnosticCode code)
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create("a"), GraphNodeGenerator.Create(
                    "b",
                    parameters: new Dictionary<string, ParamValue>
                    {
                        ["value"] = new WiredParamValue(new PortReference(sourceId, portKey))
                    })
            ],
            []);
        AssertCode(graph, code);
    }

    [Test]
    public void WiredDependenciesParticipateInCycleDetection()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create(
                    "a",
                    parameters: new Dictionary<string, ParamValue>
                    {
                        ["value"] = new WiredParamValue(new PortReference("b", "out"))
                    }),
                GraphNodeGenerator.Create(
                    "b",
                    parameters: new Dictionary<string, ParamValue>
                    {
                        ["value"] = new WiredParamValue(new PortReference("a", "out"))
                    })
            ],
            []);
        AssertCode(graph, GraphDiagnosticCode.Cycle);
    }

    [Test]
    public void ConnectionCycleIsRejected()
    {
        var graph = new StrategyGraph(
            [GraphNodeGenerator.Create("a", "Not"), GraphNodeGenerator.Create("b", "Not")],
            [GraphNodeGenerator.Connect("a", "b"), GraphNodeGenerator.Connect("b", "a")]);
        AssertCode(graph, GraphDiagnosticCode.Cycle);
    }

    [TestCase(arg1: 2, arg2: 1, arg3: 1)]
    [TestCase(arg1: 1, arg2: 4, arg3: 0)]
    [TestCase(arg1: 0, arg2: 4, arg3: 1)]
    [TestCase(arg1: 1, arg2: 201, arg3: 1)]
    [TestCase(arg1: 1, arg2: 4, arg3: 0.5)]
    public void OptimizeRangeAndStepMustFitSchema(decimal min, decimal max, decimal step)
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create(
                    "sma",
                    "SMA",
                    new Dictionary<string, ParamValue>
                    {
                        ["period"] = new DecimalOptimizeParamValue(min, max, step)
                    })
            ],
            []);
        AssertCode(graph, GraphDiagnosticCode.InvalidParameter);
    }

    [Test]
    public void InvalidTextOperationIsRejectedBeforeExecution()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create(
                    "compare",
                    "Compare",
                    new Dictionary<string, ParamValue>
                    {
                        ["op"] = new FixedParamValue(new TextParamLiteral("unsupported"))
                    })
            ],
            []);
        AssertCode(graph, GraphDiagnosticCode.InvalidParameter);
    }

    [Test]
    public void TopologicalOrderIncludesWiredDependenciesAndIsStable()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create(
                    "a",
                    parameters: new Dictionary<string, ParamValue>
                    {
                        ["value"] = new WiredParamValue(new PortReference("z", "out"))
                    }),
                GraphNodeGenerator.Create("z"),
                GraphNodeGenerator.Create("b")
            ],
            []);
        new GraphValidator().Validate(graph, GraphNodeGenerator.Definitions).IsValid.ShouldBeTrue();
        new GraphTopology().Sort(graph, GraphNodeGenerator.Definitions).ShouldBe(new[] { "b", "z", "a" });
    }

    [Test]
    public void PureCycleKeepsPhase1Code_WhenCatalogAlsoContainsTradingActions()
    {
        var graph = new StrategyGraph(
            [GraphNodeGenerator.Create("a", "Not"), GraphNodeGenerator.Create("b", "Not")],
            [GraphNodeGenerator.Connect("a", "b"), GraphNodeGenerator.Connect("b", "a")]);
        INodeDefinition[] catalog = [.. GraphNodeGenerator.Definitions, new TradingNodeDefinition(TradingNodeBehavior.MarketBuy)];

        new GraphValidator().Validate(graph, catalog)
            .Diagnostics.Select(diagnostic => diagnostic.Code)
            .ShouldBe(new[] { GraphDiagnosticCode.Cycle });
    }

    private static void AssertCode(StrategyGraph graph, GraphDiagnosticCode code)
    {
        GraphValidationResult result = new GraphValidator().Validate(graph, GraphNodeGenerator.Definitions);
        result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldContain(code);
    }
}