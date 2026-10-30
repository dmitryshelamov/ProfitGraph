using ProfitGraph.Application.Backtesting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Application.Backtesting;

/// <summary>
///     The compilation fingerprint identifies what a queued run executes; it changes only with semantics, never with labels
///     or display units.
/// </summary>
public sealed class GraphCompilationFingerprintTests
{
    private static readonly StrategyTemplateKind[] Templates = Enum.GetValues<StrategyTemplateKind>();

    /// <summary>
    ///     Pinned so that a change of the fingerprint, which invalidates queued runs, is always deliberate.
    /// </summary>
    [TestCase("Sma", "5B7464AFB036D6F4F1843FF8E6D7C7AF081EBD3B72ABA1CE3C3005E45AAA0247")]
    [TestCase("Ladder", "4F9C7C139FC0DBE3B0982F879F81B837B97730D2E1F88CDB624410C64F29B1A5")]
    public void FingerprintOfAFixedGraphIsStable(string workload, string expected)
    {
        StrategyGraph graph = workload == "Sma" ? SmaStrategyGraphGenerator.Generate() : LadderStrategyGraphGenerator.Generate();

        GraphCompilationFingerprint.Calculate(Compile(graph)).ShouldBe(expected);
    }

    [Test]
    public void EqualDecimalsWithAnotherScaleKeepTheFingerprint_AndAnotherValueChangesIt()
    {
        string fingerprint = GraphCompilationFingerprint.Calculate(Compile(SmaStrategyGraphGenerator.Generate(buyAmount: 100m)));

        GraphCompilationFingerprint.Calculate(Compile(SmaStrategyGraphGenerator.Generate(buyAmount: 100.00m))).ShouldBe(fingerprint);
        GraphCompilationFingerprint.Calculate(Compile(SmaStrategyGraphGenerator.Generate(buyAmount: 101m))).ShouldNotBe(fingerprint);
        GraphCompilationFingerprint.Calculate(Compile(SmaStrategyGraphGenerator.Generate(fastPeriod: 3, slowPeriod: 5))).ShouldNotBe(fingerprint);
    }

    [Test]
    public void NodeDisplayNamesLeaveTheFingerprintUnchanged()
    {
        StrategyGraph graph = SmaStrategyGraphGenerator.Generate();
        var renamed = new StrategyGraph(
            graph.Nodes.Select(node => new GraphNode(
                node.Id,
                node.TypeKey,
                node.SchemaVersion,
                node.Parameters,
                node.DeclaredValueType,
                node.EvaluationMode,
                node.ParameterIdentities,
                $"Renamed {node.Id}")),
            graph.Connections,
            graph.TemplateKind);

        GraphCompilationFingerprint.Calculate(Compile(renamed)).ShouldBe(GraphCompilationFingerprint.Calculate(Compile(graph)));
    }

    [TestCaseSource(nameof(Templates))]
    public void DisplayUnitsLeaveTheFingerprintUnchanged(StrategyTemplateKind kind)
    {
        StrategyDefinition definition = Template(kind);
        string fingerprint = GraphCompilationFingerprint.Calculate(Compile(definition));

        foreach (StrategyParameterDisplayUnit? unit in new StrategyParameterDisplayUnit?[]
                 {
                     null, StrategyParameterDisplayUnit.None, StrategyParameterDisplayUnit.Percent, StrategyParameterDisplayUnit.Quote, StrategyParameterDisplayUnit.Base
                 })
        {
            var changed = new StrategyDefinition(
                definition.Parameters.Select(parameter => parameter with { DisplayUnit = unit }),
                definition.Values,
                definition.Blocks,
                definition.Wires,
                definition.TemplateKind,
                definition.Direction);

            GraphCompilationFingerprint.Calculate(Compile(changed)).ShouldBe(fingerprint);
        }
    }

    [Test]
    public void EveryTemplateHasItsOwnFingerprint()
    {
        string[] fingerprints = [.. Templates.Select(kind => GraphCompilationFingerprint.Calculate(Compile(Template(kind))))];

        fingerprints.ShouldAllBe(fingerprint => fingerprint.Length == 64);
        fingerprints.Distinct().Count().ShouldBe(Templates.Length);
    }

    [Test]
    public void RepeatedCompilationGivesTheSameFingerprint()
    {
        foreach (StrategyTemplateKind kind in Templates)
        {
            GraphCompilationFingerprint.Calculate(Compile(Template(kind))).ShouldBe(GraphCompilationFingerprint.Calculate(Compile(Template(kind))));
        }
    }

    private static StrategyDefinition Template(StrategyTemplateKind kind)
    {
        return kind switch
        {
            StrategyTemplateKind.SmaCrossover => SmaStrategyBlockFactory.Create(),
            StrategyTemplateKind.Ladder => LadderStrategyBlockFactory.Create(),
            StrategyTemplateKind.LadderV2 => LadderResumeStrategyBlockFactory.Create(),
            StrategyTemplateKind.LadderProfitFox => ProfitFoxLadderStrategyBlockFactory.Create(),
            StrategyTemplateKind.LadderSma => LadderSmaStrategyBlockFactory.Create(),
            StrategyTemplateKind.CapitulationDip => CapitulationDipStrategyBlockFactory.Create(),
            StrategyTemplateKind.VolumeMomentum => VolumeMomentumStrategyBlockFactory.Create(),
            StrategyTemplateKind.LadderPlus => LadderPlusStrategyBlockFactory.Create(),
            StrategyTemplateKind.DcaProfitFox => ProfitFoxDcaStrategyBlockFactory.Create(),
            StrategyTemplateKind.Grid => GridStrategyBlockFactory.Create(),
            StrategyTemplateKind.EmaCrossover => EmaCrossoverStrategyBlockFactory.Create(),
            StrategyTemplateKind.MacdTrend => MacdTrendStrategyBlockFactory.Create(),
            StrategyTemplateKind.RsiReversion => RsiReversionStrategyBlockFactory.Create(),
            StrategyTemplateKind.BollingerReversion => BollingerReversionStrategyBlockFactory.Create(),
            StrategyTemplateKind.LevelTrade => LevelTradeStrategyBlockFactory.Create(),
            StrategyTemplateKind.LevelTradeShort => LevelTradeShortStrategyBlockFactory.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static CompiledTradingGraph Compile(StrategyDefinition definition)
    {
        return Compile(new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition).Graph);
    }

    private static CompiledTradingGraph Compile(StrategyGraph graph)
    {
        return new TradingGraphCompiler().Compile(graph, ProductionTradingNodeTests.Catalog).Graph.ShouldNotBeNull();
    }
}