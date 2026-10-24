using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Tests.Unit.Engine.Nodes.BuiltIn;

public sealed class SmaNodeDefinitionTests
{
    [Test]
    public void Evaluate_AveragesOnlyRequestedClosesThroughCurrentBar()
    {
        var node = new SmaNodeDefinition();
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["period"] = new NumberParamLiteral(2) });

        NodeOutput output = node.Evaluate(Context(barIndex: 2), new Dictionary<string, NodeOutput>(), parameters)["out"];

        output.SeriesValue.ShouldBe(13.5d);
    }

    [Test]
    public void Evaluate_RejectsInsufficientHistory()
    {
        var node = new SmaNodeDefinition();
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["period"] = new NumberParamLiteral(2) });

        Should.Throw<InvalidOperationException>(() =>
            node.Evaluate(Context(barIndex: 0), new Dictionary<string, NodeOutput>(), parameters));
    }

    private static NodeContext Context(int barIndex)
    {
        DateTimeOffset time = DateTimeOffset.UnixEpoch;
        Candle[] history =
        [
            new Candle(time, Open: 10m, High: 12m, Low: 9m, Close: 11m, Volume: 100m),
            new Candle(time.AddHours(1), Open: 11m, High: 15m, Low: 10m, Close: 14m, Volume: 100m),
            new Candle(time.AddHours(2), Open: 14m, High: 16m, Low: 12m, Close: 13m, Volume: 100m),
            new Candle(time.AddHours(3), Open: 100m, High: 120m, Low: 90m, Close: 110m, Volume: 100m)
        ];

        return new NodeContext(barIndex, history, "BTC/USDT", "1h");
    }
}