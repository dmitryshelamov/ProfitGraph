using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs.Composites;
using ProfitGraph.Engine.Graphs.Dependencies;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Проверяет структурные инварианты графа, не выполняя ноды и не обращаясь к внешним системам.
/// </summary>
public sealed class GraphValidator
{
    public GraphValidationResult Validate(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions)
    {
        return Validate(graph, definitions, strict: false).Validation;
    }

    /// <summary>
    ///     Проверка перед торговым replay; legacy metadata никогда не отключает размерностные ограничения.
    /// </summary>
    public GraphValidationResult ValidateStrict(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions)
    {
        return Validate(graph, definitions, strict: true).Validation;
    }

    /// <summary>
    ///     Строгая проверка, которая заодно отдаёт посчитанные типы выходов, такты и прогрев, чтобы компилятор не пересчитывал их.
    /// </summary>
    internal StrictGraphAnalysis AnalyzeStrict(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions)
    {
        return Validate(graph, definitions, strict: true);
    }

    private static StrictGraphAnalysis Validate(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions, bool strict)
    {
        CompositeExpansion expansion = new CompositeExpander().Expand(graph, definitions);
        StrictGraphAnalysis analysis = ValidateExpanded(expansion.Graph, definitions, strict);
        var outerDiagnostics = new List<GraphDiagnostic>();

        foreach (GraphNode node in graph.Nodes)
        {
            IExpandableNodeDefinition? definition = definitions.OfType<IExpandableNodeDefinition>().SingleOrDefault(type => type.TypeKey == node.TypeKey);

            if (definition is not null && HasInvalidMetadata(node, definition))
            {
                outerDiagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.InvalidNodeMetadata, "Node metadata is incompatible with its definition.", node.Id));
            }
        }

        return analysis with
        {
            Validation = new GraphValidationResult(
                outerDiagnostics.Concat(expansion.Diagnostics).Concat(analysis.Validation.Diagnostics.Select(expansion.MapDiagnostic)).ToArray())
        };
    }

    private static StrictGraphAnalysis ValidateExpanded(StrategyGraph graph, IReadOnlyList<INodeDefinition> definitions, bool strict)
    {
        bool validateEngine = strict;
        strict |= graph.Nodes.Any(node => node.DeclaredValueType is ValueType.Price or ValueType.BaseAmount or ValueType.QuoteAmount ||
            node.EvaluationMode.HasValue || definitions.Any(definition => definition.TypeKey == node.TypeKey &&
                (definition.IsAction || definition.Category == NodeCategory.Risk ||
                    node.TypeKey == NodeTypeKeys.CandleField || definition.Inputs.Concat(definition.Outputs)
                        .Any(port => port.ValueType is ValueType.Price or ValueType.BaseAmount or ValueType.QuoteAmount))));
        var diagnostics = new List<GraphDiagnostic>();
        Dictionary<string, INodeDefinition> types = definitions.ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);

        if (graph.Nodes.Count == 0)
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.EmptyGraph, "Graph has no nodes."));
        }

        foreach (GraphNode node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || !nodes.TryAdd(node.Id, node))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.DuplicateNodeId, "Node IDs must be non-empty and unique.", node.Id));
            }

            if (!types.TryGetValue(node.TypeKey, out INodeDefinition? definition))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.UnknownNodeType, $"Unknown node type '{node.TypeKey}'.", node.Id));

                continue;
            }

            if (definition.SchemaVersion != node.SchemaVersion)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.UnsupportedSchemaVersion, "Unsupported node schema version.", node.Id));
            }

            if (HasInvalidMetadata(node, definition))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.InvalidNodeMetadata, "Node metadata is incompatible with its definition.", node.Id));
            }

            if (node.DeclaredValueType == ValueType.Integer && node.Parameters.TryGetValue("value", out ParamValue? integerValue))
            {
                bool invalidInteger = integerValue switch
                {
                    FixedParamValue { Value: DecimalParamLiteral integer } => decimal.Truncate(integer.Value) != integer.Value,
                    DecimalOptimizeParamValue optimize => decimal.Truncate(optimize.Min) != optimize.Min || decimal.Truncate(optimize.Max) != optimize.Max ||
                        decimal.Truncate(optimize.Step) != optimize.Step,
                    _ => false
                };

                if (invalidInteger)
                {
                    diagnostics.Add(
                        new GraphDiagnostic(
                            GraphDiagnosticCode.InvalidParameter,
                            "Integer literals and optimization bounds/step must be integral.",
                            node.Id,
                            ParamKey: "value"));
                }
            }

            foreach (string key in node.Parameters.Keys.Except(
                         definition.Params.Select(param => param.Key).Concat(definition.EngineInputs.Select(input => input.Key)),
                         StringComparer.Ordinal))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.UnknownParameter, "Unknown parameter.", node.Id, ParamKey: key));
            }

            foreach (ParamSpec spec in definition.Params)
            {
                if (node.Parameters.TryGetValue(spec.Key, out ParamValue? value))
                {
                    if (!definition.EngineInputs.Any(input => input.Key == spec.Key))
                    {
                        ValidateParameter(node, spec, value, strict, diagnostics);
                    }
                }
                else if (spec.Key == "value" && (node.DeclaredValueType is ValueType.Boolean or ValueType.Tag or ValueType.OrderId or ValueType.RejectionReason
                             || (strict && node.DeclaredValueType is ValueType.Price or ValueType.BaseAmount or ValueType.QuoteAmount)))
                {
                    diagnostics.Add(
                        new GraphDiagnostic(GraphDiagnosticCode.InvalidParameter, "Typed values require an explicit matching source.", node.Id, ParamKey: spec.Key));
                }
                else if (spec.DefaultValue is null)
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.MissingParameter, "Required parameter is missing.", node.Id, ParamKey: spec.Key));
                }
            }
        }

        PortSpec? FindOutput(PortReference reference)
        {
            if (!nodes.TryGetValue(reference.NodeId, out GraphNode? source))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.UnknownNode, "Source node does not exist.", reference.NodeId, reference.PortKey));

                return null;
            }

            if (!types.TryGetValue(source.TypeKey, out INodeDefinition? type))
            {
                return null;
            }

            PortSpec? port = type.Outputs.SingleOrDefault(output => output.Key == reference.PortKey);

            if (port is null)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.UnknownPort, "Output port does not exist.", source.Id, reference.PortKey));
            }

            if (port is not null && port.Kind == PortKind.Series && source.TypeKey is NodeTypeKeys.Constant or NodeTypeKeys.Parameter &&
                source.DeclaredValueType == ValueType.Boolean)
            {
                port = port with { Kind = PortKind.Condition, ValueType = ValueType.Boolean };
            }

            if (port is not null && type.TradingBehavior == TradingNodeBehavior.EventFields
                && NodeTextSettingResolver.TryResolve(source, type.Params, "field", out string key))
            {
                EventFieldSpec? field = EventFieldCatalog.Find(key);

                if (field is not null)
                {
                    port = port with { ValueType = field.ValueType, Kind = field.ValueType == ValueType.Boolean ? PortKind.Condition : PortKind.Series };
                }
            }

            return port;
        }

        var connected = new HashSet<PortReference>();

        foreach (GraphConnection connection in graph.Connections)
        {
            PortSpec? source = FindOutput(connection.Source);

            if (!nodes.TryGetValue(connection.Target.NodeId, out GraphNode? target))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.UnknownNode, "Target node does not exist.", connection.Target.NodeId));

                continue;
            }

            if (!types.TryGetValue(target.TypeKey, out INodeDefinition? type))
            {
                continue;
            }

            PortSpec? input = type.Inputs.SingleOrDefault(port => port.Key == connection.Target.PortKey);

            if (input is null)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.UnknownPort, "Input port does not exist.", target.Id, connection.Target.PortKey));
            }
            else if (source is not null && input.Kind != source.Kind)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatiblePort, "Source and target port kinds differ.", target.Id, input.Key));
            }

            if (!connected.Add(connection.Target))
            {
                diagnostics.Add(
                    new GraphDiagnostic(GraphDiagnosticCode.MultipleInputConnections, "An input accepts only one connection.", target.Id, connection.Target.PortKey));
            }
        }

        foreach (GraphNode node in graph.Nodes)
        {
            if (!types.TryGetValue(node.TypeKey, out INodeDefinition? definition))
            {
                continue;
            }

            foreach (PortSpec input in definition.Inputs)
            {
                var target = new PortReference(node.Id, input.Key);
                EngineInputSpec? engineInput = definition.EngineInputs.SingleOrDefault(spec => spec.Key == input.Key);
                bool candleTrigger = definition.IsAction && input.Kind == PortKind.Event && node.EvaluationMode == ActionEvaluationMode.OnCandleClose;

                if (!connected.Contains(target) && !candleTrigger && engineInput is null)
                {
                    diagnostics.Add(
                        new GraphDiagnostic(
                            definition.IsAction && input.Kind == PortKind.Event ? GraphDiagnosticCode.MissingTrigger : GraphDiagnosticCode.MissingInput,
                            "Required input is not connected.",
                            node.Id,
                            input.Key));
                }
            }

            if (definition.IsAction && node.EvaluationMode == ActionEvaluationMode.OnCandleClose
                && graph.Connections.Any(connection => connection.Target.NodeId == node.Id
                    && definition.Inputs.Any(input => input.Key == connection.Target.PortKey && input.Kind == PortKind.Event)))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.TriggerWithCandleClockMode, "Candle-close mode cannot also have an external trigger.", node.Id));
            }

            if (definition.TradingBehavior == TradingNodeBehavior.CancelOrder && node.EvaluationMode == ActionEvaluationMode.OnCandleClose)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.InvalidNodeMetadata, "Cancellation supports event mode only.", node.Id));
            }

            foreach (EngineInputSpec input in definition.EngineInputs)
            {
                if (!definition.Inputs.Any(port => port.Key == input.Key && port.ValueType == input.ValueType))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.InvalidNodeMetadata, "Engine input metadata must match its value port.", node.Id, input.Key));

                    continue;
                }

                if (connected.Contains(new PortReference(node.Id, input.Key)))
                {
                    continue;
                }

                if (node.Parameters.TryGetValue(input.Key, out ParamValue? value))
                {
                    if (!input.SupportsInline || !EngineLiteralResolver.IsValid(value, input.ValueType))
                    {
                        diagnostics.Add(
                            new GraphDiagnostic(
                                GraphDiagnosticCode.InvalidLiteralRange,
                                "Inline input must match its exact declared type and valid range.",
                                node.Id,
                                input.Key));
                    }
                }
                else if (input.Required && input.DefaultValue is null)
                {
                    diagnostics.Add(
                        new GraphDiagnostic(
                            GraphDiagnosticCode.MissingRequiredValueSource,
                            "Required value has no connection, inline source or typed default.",
                            node.Id,
                            input.Key));
                }
            }

            if (definition.TradingBehavior == TradingNodeBehavior.OrderSizePreview)
            {
                string Setting(string key, string fallback) =>
                    node.Parameters.GetValueOrDefault(key) is FixedParamValue { Value: TextParamLiteral text } ? text.Value : fallback;

                bool HasSource(string key) => connected.Contains(new PortReference(node.Id, key)) || node.Parameters.ContainsKey(key);

                // A short plan's buy covers a base quantity (ADR 0061).
                bool buy = Setting("side", "Buy") == "Buy" &&
                    !graph.Nodes.Any(candidate => candidate.TypeKey is nameof(TradingNodeBehavior.MarketCover) or nameof(TradingNodeBehavior.LimitCover));
                bool limit = Setting("type", "Market") == "Limit";

                if (!HasSource(buy ? "amount" : "quantity") || HasSource(buy ? "quantity" : "amount") || limit != HasSource("price"))
                {
                    diagnostics.Add(
                        new GraphDiagnostic(
                            GraphDiagnosticCode.MissingRequiredValueSource,
                            "Preview requires its selected side's amount and a price exactly for limit type.",
                            node.Id));
                }
            }

            if (definition.TradingBehavior == TradingNodeBehavior.CancelOrder)
            {
                bool HasTarget(string key) => connected.Contains(new PortReference(node.Id, key)) || node.Parameters.ContainsKey(key);

                if (HasTarget("orderId") == HasTarget("tag"))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.ConflictingCancelTarget, "Cancellation requires exactly one order ID or tag.", node.Id));
                }
            }

            foreach ((string key, ParamValue parameter) in node.Parameters)
            {
                if (parameter is not WiredParamValue wired || !definition.EngineInputs.Any(input => input.Key == key)
                    || !GraphDependencyPolicy.IsActiveParameter(graph, node, key, definition))
                {
                    continue;
                }

                PortSpec? source = FindOutput(wired.Source);
                EngineInputSpec? input = definition.EngineInputs.SingleOrDefault(spec => spec.Key == key);

                if (input is not null && source?.Kind == PortKind.Event)
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatiblePort, "Value inputs cannot be wired to events.", node.Id, input.Key));
                }
            }

            foreach ((string key, ParamValue value) in node.Parameters)
            {
                if (value is not WiredParamValue wired || definition.EngineInputs.Any(input => input.Key == key))
                {
                    continue;
                }

                PortSpec? source = FindOutput(wired.Source);
                ParamSpec? spec = definition.Params.SingleOrDefault(param => param.Key == key);

                if (source is not null && spec is not null && (source.Kind != PortKind.Series || spec.ValueKind != ParamValueKind.Number))
                {
                    diagnostics.Add(
                        new GraphDiagnostic(GraphDiagnosticCode.IncompatiblePort, "Wired parameters require a numeric Series source.", node.Id, ParamKey: key));
                }
            }
        }

        if (new GraphTopology().Sort(graph, definitions) is null)
        {
            diagnostics.Add(
                new GraphDiagnostic(
                    graph.Nodes.Any(node => types.TryGetValue(node.TypeKey, out INodeDefinition? definition) && definition.IsAction)
                        ? GraphDiagnosticCode.ValueOrImmediateActionCycle
                        : GraphDiagnosticCode.Cycle,
                    "Immediate dependencies contain a cycle."));
        }

        IReadOnlyDictionary<PortReference, ValueType>? outputTypes = null;
        IReadOnlyDictionary<string, NodeClock>? clocks = null;
        var warmup = 0;

        if (diagnostics.Count == 0)
        {
            new EventPayloadInference().Validate(graph, definitions, diagnostics);
            outputTypes = new GraphValueTypeResolver().Resolve(graph, definitions, diagnostics);

            if (validateEngine && diagnostics.Count == 0)
            {
                clocks = new NodeClockInference().Resolve(graph, definitions, diagnostics);
                warmup = new WarmUpCalculator().Calculate(graph, definitions, clocks, diagnostics);
            }
        }

        return new StrictGraphAnalysis(new GraphValidationResult(diagnostics.AsReadOnly()), graph, outputTypes, clocks, warmup);
    }

    /// <summary>
    ///     Метаданные узла не согласуются с определением: объявленный тип не у Constant/Parameter
    ///     или вне допустимых, режим вычисления у не-действия или не из разрешённых, действие без режима, неизвестный такт движка.
    /// </summary>
    private static bool HasInvalidMetadata(GraphNode node, INodeDefinition definition)
    {
        bool invalidDeclaredType = node.DeclaredValueType.HasValue && (node.TypeKey is not (NodeTypeKeys.Constant or NodeTypeKeys.Parameter) ||
            (!UnitAlgebra.IsNumeric(node.DeclaredValueType.Value) &&
                node.DeclaredValueType is not (ValueType.Boolean or ValueType.Tag or ValueType.OrderId or ValueType.RejectionReason)));
        bool invalidMode = node.EvaluationMode.HasValue &&
            (!Enum.IsDefined(node.EvaluationMode.Value) || !definition.IsAction ||
                (definition.AllowedEvaluationModes.Count > 0 && !definition.AllowedEvaluationModes.Contains(node.EvaluationMode.Value)));
        bool missingMode = definition.IsAction && !node.EvaluationMode.HasValue;
        bool invalidClock = definition.EngineClock.HasValue && !Enum.IsDefined(definition.EngineClock.Value);

        return invalidDeclaredType || invalidMode || missingMode || invalidClock;
    }

    private static bool InDecimalRange(decimal value, ParamSpec spec)
    {
        return (!spec.MinValue.HasValue || value >= (decimal)spec.MinValue.Value)
            && (!spec.MaxValue.HasValue || value <= (decimal)spec.MaxValue.Value)
            && (!spec.Step.HasValue || value % (decimal)spec.Step.Value == 0m);
    }

    private static void ValidateParameter(GraphNode node, ParamSpec spec, ParamValue value, bool strict, List<GraphDiagnostic> diagnostics)
    {
        bool exactTyped = value is DecimalOptimizeParamValue or FixedParamValue
            {
                Value: DecimalParamLiteral or BooleanParamLiteral or OrderIdParamLiteral or RejectionReasonParamLiteral
            }
            || node.DeclaredValueType is ValueType.Boolean or ValueType.Tag or ValueType.OrderId or ValueType.RejectionReason;
        bool valid = exactTyped && node.TypeKey is NodeTypeKeys.Constant or NodeTypeKeys.Parameter && spec.Key == "value"
            ? EngineLiteralResolver.IsValid(value, node.DeclaredValueType ?? ValueType.Scalar)
            : value switch
            {
                FixedParamValue { Value: TextParamLiteral text } => spec.ValueKind == ParamValueKind.Text && text.Value is not null &&
                    (spec.AllowedValues is null || spec.AllowedValues.Contains(text.Value, StringComparer.Ordinal)),
                FixedParamValue { Value: DecimalParamLiteral number } => spec.ValueKind == ParamValueKind.Number && InDecimalRange(number.Value, spec),
                DecimalOptimizeParamValue range => spec.ValueKind == ParamValueKind.Number && range.Min <= range.Max && range.Step > 0m
                    && InDecimalRange(range.Min, spec) && InDecimalRange(range.Max, spec)
                    && (!spec.Step.HasValue || range.Step % (decimal)spec.Step.Value == 0m),
                WiredParamValue => true,
                _ => false
            };

        if (!valid)
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.InvalidParameter, "Invalid parameter type, range or step.", node.Id, ParamKey: spec.Key));
        }
    }
}