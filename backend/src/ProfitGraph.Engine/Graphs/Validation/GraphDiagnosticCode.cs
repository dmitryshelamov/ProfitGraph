namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Коды структурных ошибок графа, общие для валидатора и результатов проверки.
///     Позволяют различать причины ошибки без разбора текста сообщения.
/// </summary>
public enum GraphDiagnosticCode
{
    EmptyGraph,
    DuplicateNodeId,
    UnknownNodeType,
    UnsupportedSchemaVersion,
    UnknownNode,
    UnknownPort,
    IncompatiblePort,
    MultipleInputConnections,
    MissingInput,
    UnknownParameter,
    MissingParameter,
    InvalidParameter,
    Cycle,
    IncompatibleUnits,
    MissingTrigger,
    TriggerWithCandleClockMode,
    MissingRequiredValueSource,
    InvalidLiteralRange,
    ConflictingCancelTarget,
    ValueOrImmediateActionCycle,
    InvalidNodeMetadata,
    SmaInputNotCandleClocked,
    UnboundedIndicatorPeriod,
    WarmupOverflow,
    IncompatibleEventPayload
}