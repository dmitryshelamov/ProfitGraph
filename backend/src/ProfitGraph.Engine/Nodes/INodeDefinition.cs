using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Контракт типа ноды: идентификатор, категория, версия схемы, порты, параметры и вычисление выходов.
///     Объединяет метаданные для каталога с поведением, которое вызывает интерпретатор.
/// </summary>
/// <remarks>
///     Реализации предназначены для будущего NodeTypeRegistry и публикации метаданных через GET /api/node-types.
///     Результат Evaluate содержит значения выходов, индексированные по ключам соответствующих портов.
/// </remarks>
public interface INodeDefinition
{
    string TypeKey { get; }

    bool IsIndicator => false;
    bool IsCrossover => false;
    bool IsPureComputation => false;
    bool IsCandleClocked => false;
    bool EvaluatesUnavailableInputs => false;
    bool EvaluatesDispatch => false;
    bool ProjectsTradingState => false;
    IReadOnlyList<string> SampleInputKeys => Array.Empty<string>();
    string? WarmupPeriodKey => null;
    int WarmupOffset => 0;

    bool AcceptsUnavailableInputs => false;

    bool IsAction => false;

    IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes => Array.Empty<ActionEvaluationMode>();

    TradingNodeBehavior? TradingBehavior => null;

    IReadOnlyList<EventFieldSpec> EventFields => Array.Empty<EventFieldSpec>();

    NodeClock? EngineClock => null;

    IReadOnlyList<EngineInputSpec> EngineInputs => Array.Empty<EngineInputSpec>();

    NodeCategory Category { get; }

    int SchemaVersion { get; }

    IReadOnlyList<PortSpec> Inputs { get; }

    IReadOnlyList<PortSpec> Outputs { get; }

    IReadOnlyList<ParamSpec> Params { get; }

    NodeClock ResolveClock(NodeClock inherited)
    {
        return IsAction ? NodeClock.Action : EngineClock ?? NodeClock.Dispatch;
    }

    ValueType ResolveParameterType(GraphNode node, ParamSpec spec)
    {
        return spec.Key == "period" ? ValueType.Integer :
            spec.ValueKind == ParamValueKind.Text ? ValueType.Tag : ValueType.Scalar;
    }

    ValueType? ResolveOutputType(
        GraphNode node,
        Func<string, ValueType?> input,
        List<GraphDiagnostic> diagnostics)
    {
        return null;
    }

    (decimal Min, decimal Max)? ResolveStaticBounds(
        GraphNode node,
        Func<ParamValue, (decimal Min, decimal Max)?> bound,
        Func<string, (decimal Min, decimal Max)?> inputBound)
    {
        return null;
    }

    EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        throw new InvalidOperationException($"Unsupported decimal computational node '{TypeKey}'.");
    }

    void EvaluateTrading(NodeTradingEvaluationContext context)
    {
        throw new InvalidOperationException("Unsupported state projection behavior.");
    }
}