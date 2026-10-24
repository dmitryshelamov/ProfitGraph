using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class RejectionReasonLiteralTests
{
    [TestCase("Constant")]
    [TestCase("Parameter")]
    public void ConcreteLiteralHasSameTypeEqualityAndRefusesPhase1(string type)
    {
        var graph = new StrategyGraph(
            [
                Node(
                    "a",
                    type,
                    new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new RejectionReasonParamLiteral(RejectionReason.ZeroQuantity)) },
                    ValueType.RejectionReason),
                Node(
                    "b",
                    "Constant",
                    new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new RejectionReasonParamLiteral(RejectionReason.ZeroQuantity)) },
                    ValueType.RejectionReason),
                Node("compare", "Compare", new Dictionary<string, ParamValue> { ["op"] = Text("=") })
            ],
            [Wire("a", "compare", "a"), Wire("b", "compare", "b")]);
        new GraphValidator().ValidateStrict(graph, Catalog).Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
        new CandleValueEvaluator(compiled).ReadStaticValues()[new PortReference("compare", "out")].Boolean.ShouldBeTrue();
    }

    [Test]
    public void RawNumbersAndTagsCannotMasqueradeAsRejectionReason()
    {
        foreach (ParamValue value in new ParamValue[] { Number(2m), Text("ZeroQuantity"), new FixedParamValue(new RejectionReasonParamLiteral((RejectionReason)999)) })
        {
            var graph = new StrategyGraph([Node("value", "Constant", new Dictionary<string, ParamValue> { ["value"] = value }, ValueType.RejectionReason)], []);
            new GraphValidator().ValidateStrict(graph, Catalog).IsValid.ShouldBeFalse();
            new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldBeNull();
        }
    }
}