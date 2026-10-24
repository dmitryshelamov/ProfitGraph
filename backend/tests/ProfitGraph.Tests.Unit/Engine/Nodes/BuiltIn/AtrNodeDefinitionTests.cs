using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Tests.Unit.Engine.Nodes.BuiltIn;

public sealed class AtrNodeDefinitionTests
{
    [Test]
    public void Evaluate_AveragesTrueRangesIncludingPriceGapsWithoutFutureBars()
    {
        var node = new AtrNodeDefinition();
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["period"] = new NumberParamLiteral(2) });

        NodeOutput output = node.Evaluate(Context(barIndex: 2), new Dictionary<string, NodeOutput>(), parameters)["out"];

        // True ranges are 5 and 3; candle high-low ranges alone would yield 2 and 1.
        output.SeriesValue.ShouldBe(4d);
    }

    [Test]
    public void Evaluate_RequiresPreviousCloseBeforeTheRequestedPeriod()
    {
        var node = new AtrNodeDefinition();
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["period"] = new NumberParamLiteral(2) });

        NodeOutput output = node.Evaluate(Context(barIndex: 1), new Dictionary<string, NodeOutput>(), parameters)["out"];

        output.IsAvailable.ShouldBeFalse();
    }

    private static NodeContext Context(int barIndex)
    {
        DateTimeOffset time = DateTimeOffset.UnixEpoch;
        Candle[] history =
        [
            new Candle(time, Open: 10m, High: 11m, Low: 9m, Close: 10m, Volume: 100m),
            new Candle(time.AddHours(1), Open: 13m, High: 15m, Low: 13m, Close: 14m, Volume: 100m),
            new Candle(time.AddHours(2), Open: 12m, High: 12m, Low: 11m, Close: 11.5m, Volume: 100m),
            new Candle(time.AddHours(3), Open: 100m, High: 120m, Low: 90m, Close: 110m, Volume: 100m)
        ];

        return new NodeContext(barIndex, history, "BTC/USDT", "1h");
    }
}