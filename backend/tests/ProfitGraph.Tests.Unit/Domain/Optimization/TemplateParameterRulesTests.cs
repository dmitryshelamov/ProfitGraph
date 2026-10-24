using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Domain.Optimization;

public sealed class TemplateParameterRulesTests
{
    private static readonly StrategyDefinition[] Templates =
    [
        SmaStrategyBlockFactory.Create(), LadderStrategyBlockFactory.Create(), LadderResumeStrategyBlockFactory.Create(), LadderSmaStrategyBlockFactory.Create(),
        ProfitFoxLadderStrategyBlockFactory.Create(), LadderPlusStrategyBlockFactory.Create(), ProfitFoxDcaStrategyBlockFactory.Create(),
        CapitulationDipStrategyBlockFactory.Create(), VolumeMomentumStrategyBlockFactory.Create()
    ];

    [TestCaseSource(nameof(Templates))]
    public void DefaultTemplateValues_SatisfyTheirRules(StrategyDefinition definition)
    {
        definition.TemplateKind.ShouldNotBeNull();
        Check(definition).ShouldBeEmpty();
    }

    [Test]
    public void BrokenRelations_IdentifyTheParameterNodeAndValue()
    {
        AssertSingle(SmaStrategyBlockFactory.Create(fastPeriod: 4, slowPeriod: 3), "fastPeriod", "SMA requires fastPeriod < slowPeriod.");
        AssertSingle(LadderStrategyBlockFactory.Create(drop: 1m), "drop", "Ladder requires 0 < drop < 1.");
        AssertSingle(LadderStrategyBlockFactory.Create(buyAmount: 301m), "allocationCap", "Ladder requires buyAmount <= allocationCap.");
        AssertSingle(LadderPlusStrategyBlockFactory.Create(stopLoss: 1m), "stopLoss", "Ladder+ requires 0 < stopLoss < 1.");
        AssertSingle(ProfitFoxDcaStrategyBlockFactory.Create(buyAmount: 1001m), "allocationCap", "DCA requires buyAmount <= allocationCap.");
        AssertSingle(CapitulationDipStrategyBlockFactory.Create(drop: 1m), "drop", "Capitulation dip requires 0 < drop < 1.");
        AssertSingle(VolumeMomentumStrategyBlockFactory.Create(stopLoss: 1m), "stopLoss", "Volume momentum requires 0 < stopLoss < 1.");
    }

    [Test]
    public void NonPositiveValue_IsReportedOnceAndSkipsItsRelations()
    {
        AssertSingle(SmaStrategyBlockFactory.Create(buyAmount: 0m), "buyAmount", "Template parameter 'buyAmount' must be positive.");
        AssertSingle(LadderStrategyBlockFactory.Create(allocationCap: 0m), "allocationCap", "Template parameter 'allocationCap' must be positive.");
        AssertSingle(ProfitFoxDcaStrategyBlockFactory.Create(interval: 0), "interval", "Template parameter 'interval' must be positive.");
    }

    [Test]
    public void UnmarkedStrategy_HasNoTemplateRules()
    {
        StrategyDefinition marked = SmaStrategyBlockFactory.Create(fastPeriod: 4, slowPeriod: 3);
        CompiledTradingGraph graph = Compile(marked);
        TemplateParameterRules.Check(template: null, graph, EngineExecutionInputs.Empty).ShouldBeEmpty();
    }

    [Test]
    public void OptimizedSelections_DecideTheRelationAndAMissingChoiceIsReported()
    {
        var ranges = new Dictionary<string, DecimalOptimizeParamValue> { ["fastPeriod"] = new DecimalOptimizeParamValue(Min: 1m, Max: 4m, Step: 1m) };
        StrategyDefinition definition = SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 3);
        CompiledTradingGraph graph = Compile(definition, ranges);
        OptimizationParameterRange range = OptimizationSearchSpace.FromGraph(graph).Parameters.ShouldHaveSingleItem();
        range.Reference.ShouldBe(new PortReference("fastPeriod", "value"));
        TemplateParameterRules.Check(definition.TemplateKind, graph, EngineExecutionInputs.Empty).ShouldNotBeEmpty();
        TemplateParameterRules.Check(definition.TemplateKind, graph, Select(range.Reference, value: 2m)).ShouldBeEmpty();
        GraphDiagnostic diagnostic = TemplateParameterRules.Check(definition.TemplateKind, graph, Select(range.Reference, value: 4m)).ShouldHaveSingleItem();
        diagnostic.Message.ShouldBe("SMA requires fastPeriod < slowPeriod.");
        diagnostic.NodeId.ShouldBe(StrategyDefinitionLowerer.ParameterNodeId("fastPeriod"));
    }

    private static void AssertSingle(StrategyDefinition definition, string parameter, string message)
    {
        GraphDiagnostic diagnostic = Check(definition).ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe(GraphDiagnosticCode.InvalidParameter);
        diagnostic.NodeId.ShouldBe(StrategyDefinitionLowerer.ParameterNodeId(parameter));
        diagnostic.ParamKey.ShouldBe("value");
        diagnostic.Message.ShouldBe(message);
    }

    private static IReadOnlyList<GraphDiagnostic> Check(StrategyDefinition definition)
    {
        return TemplateParameterRules.Check(definition.TemplateKind, Compile(definition), EngineExecutionInputs.Empty);
    }

    private static CompiledTradingGraph Compile(StrategyDefinition definition, IReadOnlyDictionary<string, DecimalOptimizeParamValue>? ranges = null)
    {
        StrategyGraph graph = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition, ranges).Graph;
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, ProductionTradingNodeTests.Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }

    private static EngineExecutionInputs Select(PortReference reference, decimal value)
    {
        return new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [reference] = value });
    }
}