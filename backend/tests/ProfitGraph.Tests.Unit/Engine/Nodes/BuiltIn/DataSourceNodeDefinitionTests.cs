using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Tests.Unit.Engine.Nodes.BuiltIn;

public sealed class DataSourceNodeDefinitionTests
{
    [Test]
    public void Evaluate_ReturnsCurrentCloseWithoutReadingFutureBars()
    {
        DateTimeOffset time = DateTimeOffset.UnixEpoch;
        Candle[] history =
        [
            new Candle(time, Open: 10m, High: 12m, Low: 9m, Close: 11m, Volume: 100m),
            new Candle(time.AddHours(1), Open: 100m, High: 120m, Low: 90m, Close: 110m, Volume: 100m)
        ];
        var context = new NodeContext(barIndex: 0, history, "BTC/USDT", "1h");
        var node = new DataSourceNodeDefinition();

        NodeOutput output = node.Evaluate(
            context,
            new Dictionary<string, NodeOutput>(),
            new ResolvedParams(new Dictionary<string, ParamLiteral>()))["out"];

        output.ShouldSatisfyAllConditions(
            () => output.Kind.ShouldBe(PortKind.Series),
            () => output.SeriesValue.ShouldBe(11d));
    }
}