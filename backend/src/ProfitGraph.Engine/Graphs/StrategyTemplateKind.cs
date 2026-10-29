namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Optional editable-template validation identity; the replay engine never branches on it.
/// </summary>
public enum StrategyTemplateKind
{
    SmaCrossover,
    Ladder,
    LadderV2,
    LadderProfitFox,
    LadderSma,
    CapitulationDip,
    VolumeMomentum,
    LadderPlus,
    DcaProfitFox,
    Grid,
    EmaCrossover,
    MacdTrend,
    RsiReversion,
    BollingerReversion,
    LevelTrade
}