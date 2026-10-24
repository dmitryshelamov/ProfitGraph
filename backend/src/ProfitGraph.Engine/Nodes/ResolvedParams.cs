using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Доступ к уже разрешённым значениям параметров для текущего вычисления, а не к их описаниям ParamSpec.
///     Нода читает число или текст, не зная, откуда получено значение.
/// </summary>
/// <remarks>
///     GraphParamResolver отвечает за источники Fixed, Optimize и Wired: фиксированное значение, текущую точку sweep
///     или результат исходной ноды. Этот класс предоставляет только доступ к переданным значениям.
/// </remarks>
public sealed class ResolvedParams
{
    private readonly IReadOnlyDictionary<string, ParamLiteral> _values;

    public ResolvedParams(IReadOnlyDictionary<string, ParamLiteral> values)
    {
        _values = values;
    }

    public double GetNumber(string paramKey)
    {
        return GetValue(paramKey) is NumberParamLiteral number
            ? number.Value
            : throw new ParameterResolutionException(paramKey, "must be a number.");
    }

    public int GetInt(string paramKey)
    {
        double value = GetNumber(paramKey);

        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new ParameterResolutionException(paramKey, "must be an integer.");
        }

        return (int)value;
    }

    public string GetString(string paramKey)
    {
        return GetValue(paramKey) is TextParamLiteral { Value: not null } text
            ? text.Value
            : throw new ParameterResolutionException(paramKey, "must be text.");
    }

    private ParamLiteral GetValue(string paramKey)
    {
        return _values.TryGetValue(paramKey, out ParamLiteral? value) && value is not null
            ? value
            : throw new ParameterResolutionException(paramKey, "has no resolved value.");
    }
}