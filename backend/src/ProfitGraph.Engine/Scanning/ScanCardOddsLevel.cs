namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     One level of <see cref="ScanCardOddsModel" />: its shift of the random walk's log-odds, its shrunk share of targets, how many
///     trades that share is worth with the imagined ones and the level's mean random walk chance.
/// </summary>
internal readonly record struct ScanCardOddsLevel(double Shift, double Share, double Strength, double Geometry);