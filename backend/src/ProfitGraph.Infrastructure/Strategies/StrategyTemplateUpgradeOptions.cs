namespace ProfitGraph.Infrastructure.Strategies;

/// <summary>
///     Section <c>Strategies:TemplateUpgrades</c>: when the Worker's <c>live</c> role brings saved strategies forward to the
///     later revision of their template (ADR 0038).
/// </summary>
internal sealed class StrategyTemplateUpgradeOptions
{
    internal const string SectionName = "Strategies:TemplateUpgrades";

    public bool Enabled { get; init; } = true;

    /// <summary>
    ///     The pause after the role starts before the first pass.
    /// </summary>
    public TimeSpan FirstPassDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     The pause after a failed pass before the next one.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(1);

    public int MaxPasses { get; init; } = 3;

    public bool IsValid()
    {
        return FirstPassDelay >= TimeSpan.Zero && RetryDelay >= TimeSpan.Zero && MaxPasses > 0;
    }
}