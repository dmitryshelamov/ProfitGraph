using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Strategies.Templates;

internal static class StrategyTemplateLayouts
{
    // The editor measures rendered node sizes and lays positionless templates out with ELK on first open.
    public static GraphLayout Sma()
    {
        return Empty();
    }

    public static GraphLayout Ladder()
    {
        return Empty();
    }

    public static GraphLayout CapitulationDip()
    {
        return new GraphLayout(
            new Dictionary<string, NodePosition>(StringComparer.Ordinal)
            {
                ["clock"] = new NodePosition(X: 32, Y: 32),
                ["entryConditions"] = new NodePosition(X: 328, Y: 32),
                ["entry"] = new NodePosition(X: 624, Y: 32),
                ["exitConditions"] = new NodePosition(X: 328, Y: 400),
                ["exitSignal"] = new NodePosition(X: 624, Y: 400),
                ["exit"] = new NodePosition(X: 920, Y: 400)
            });
    }

    // Volume momentum mirrors the capitulation dip block identities.
    public static GraphLayout VolumeMomentum()
    {
        return CapitulationDip();
    }

    private static GraphLayout Empty()
    {
        return new GraphLayout(new Dictionary<string, NodePosition>(StringComparer.Ordinal));
    }
}