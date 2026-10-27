using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot's values of its strategy's parameters by name. Whether they match the strategy version is checked where
///     the version is known; here only names must be present.
/// </summary>
public sealed record BotParameters
{
    public IReadOnlyDictionary<string, decimal> Values { get; }

    public BotParameters(IReadOnlyDictionary<string, decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "A bot parameter needs a name.");
        }

        Values = new SortedDictionary<string, decimal>(values.ToDictionary(), StringComparer.Ordinal);
    }

    public bool Equals(BotParameters? other)
    {
        return other is not null && Values.Count == other.Values.Count &&
            Values.All(pair => other.Values.TryGetValue(pair.Key, out decimal value) && value == pair.Value);
    }

    /// <summary>
    ///     The values for a strategy version that names <paramref name="parameters" /> (ADR 0039): a value of a parameter the
    ///     version still has is kept, a new parameter takes its default and a value of a removed parameter is dropped.
    /// </summary>
    public BotParameters Fit(IReadOnlyList<StrategyParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return new BotParameters(
            parameters.DistinctBy(parameter => parameter.Name, StringComparer.Ordinal)
                .ToDictionary(
                    parameter => parameter.Name,
                    parameter => Values.TryGetValue(parameter.Name, out decimal value) ? value : parameter.DefaultValue,
                    StringComparer.Ordinal));
    }

    public override int GetHashCode()
    {
        return Values.Count;
    }
}