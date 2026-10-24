using System.Collections.ObjectModel;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Typed parameter snapshots, or an internal resolver-owned replay view, without floating-point accounting conversions.
/// </summary>
public sealed class ResolvedEngineParameters
{
    private readonly Dictionary<string, EngineValue> _values;
    private int _unavailableCount;

    internal long Version { get; private set; }
    internal bool HasUnavailableValues => _unavailableCount != 0;
    public IReadOnlyDictionary<string, EngineValue> Values { get; }
    public IReadOnlyDictionary<string, string> TextSettings { get; }

    public EngineValue this[string key] => Values[key];

    public ResolvedEngineParameters(IDictionary<string, EngineValue> values, IDictionary<string, string> textSettings)
        : this(new Dictionary<string, EngineValue>(values, StringComparer.Ordinal), new Dictionary<string, string>(textSettings, StringComparer.Ordinal))
    {
    }

    private ResolvedEngineParameters(Dictionary<string, EngineValue> values, Dictionary<string, string> textSettings)
    {
        _values = values;

        foreach (EngineValue value in values.Values)
        {
            if (!value.IsAvailable)
            {
                _unavailableCount++;
            }
        }

        Values = new ReadOnlyDictionary<string, EngineValue>(values);
        TextSettings = new ReadOnlyDictionary<string, string>(textSettings);
    }

    internal void Update(string key, EngineValue value)
    {
        EngineValue previous = _values[key];

        if (previous.Equals(value))
        {
            return;
        }

        _unavailableCount += (value.IsAvailable ? 0 : 1) - (previous.IsAvailable ? 0 : 1);
        _values[key] = value;
        Version++;
    }

    internal static ResolvedEngineParameters FromOwned(Dictionary<string, EngineValue> values, Dictionary<string, string> textSettings)
    {
        return new ResolvedEngineParameters(values, textSettings);
    }
}