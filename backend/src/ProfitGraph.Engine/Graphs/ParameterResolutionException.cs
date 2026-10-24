using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Ошибка разрешения или чтения параметра: отсутствующее значение, неверный тип либо нарушение ограничений.
/// </summary>
public sealed class ParameterResolutionException(string paramKey, string reason)
    : EngineException($"Parameter '{paramKey}' {reason}")
{
    public string ParamKey { get; } = paramKey;
}