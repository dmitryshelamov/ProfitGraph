using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Resolves exact fixed, optimized and wired values against copied compilation metadata.
/// </summary>
public sealed class EngineParameterResolver
{
    private readonly Dictionary<(CompiledTradingNode Node, EngineExecutionInputs Execution), ResolvedEngineParameters> _resolvedParameters =
        new Dictionary<(CompiledTradingNode Node, EngineExecutionInputs Execution), ResolvedEngineParameters>();

    public ResolvedEngineParameters Resolve(CompiledTradingNode node, IReadOnlyDictionary<PortReference, EngineValue> outputs, EngineExecutionInputs execution)
    {
        if (_resolvedParameters.TryGetValue((node, execution), out ResolvedEngineParameters? cached) &&
            (!node.HasDynamicParameters || DynamicValuesMatch(node, outputs, cached)))
        {
            return cached;
        }

        ResolvedEngineParameters resolved = Create(node, outputs, execution);
        _resolvedParameters[(node, execution)] = resolved;

        return resolved;
    }

    /// <summary>
    ///     Returns a resolver-owned view consumed immediately by one replay, never a retained public snapshot.
    /// </summary>
    internal ResolvedEngineParameters ResolveForReplay(
        CompiledTradingNode node,
        EngineValue[] outputs,
        EngineExecutionInputs execution,
        ResolvedEngineParameters?[] storage,
        int nodePosition)
    {
        ResolvedEngineParameters? resolved = storage[nodePosition];

        if (resolved is null)
        {
            resolved = CreateForReplay(node, outputs, execution);
            storage[nodePosition] = resolved;

            return resolved;
        }

        if (!node.HasDynamicParameters)
        {
            return resolved;
        }

        for (var index = 0; index < node.UnboundParameterSpecs.Count; index++)
        {
            string key = node.UnboundParameterSpecs[index].Key;

            if (!node.WiredParameterSourceSlots.TryGetValue(key, out int slot))
            {
                continue;
            }

            EngineValue value = outputs[slot];
            ValueType type = resolved[key].ValueType;

            if (value.ValueType != type)
            {
                throw new InvalidOperationException($"Parameter '{key}' requires {type}, got {value.ValueType}.");
            }

            resolved.Update(key, value);
        }

        for (var index = 0; index < node.EngineInputs.Count; index++)
        {
            CompiledEngineInput input = node.EngineInputs[index];

            if (input.SourceSlot < 0)
            {
                continue;
            }

            EngineValue value = outputs[input.SourceSlot];

            if (input.Source is null && value.ValueType != input.Spec.ValueType)
            {
                throw new InvalidOperationException($"Parameter '{input.Spec.Key}' requires {input.Spec.ValueType}, got {value.ValueType}.");
            }

            resolved.Update(input.Spec.Key, value);
        }

        return resolved;
    }

    private static ResolvedEngineParameters CreateForReplay(CompiledTradingNode node, EngineValue[] outputs, EngineExecutionInputs execution)
    {
        var values = new Dictionary<string, EngineValue>(StringComparer.Ordinal);
        var textSettings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ParamSpec spec in node.UnboundParameterSpecs)
        {
            ValueType type = node.Definition.ResolveParameterType(node.Node, spec);
            ParamValue value = node.Node.Parameters.GetValueOrDefault(spec.Key) ?? new FixedParamValue(Default(spec.DefaultValue));

            if (spec.ValueKind == ParamValueKind.Text)
            {
                textSettings[spec.Key] = NodeTextSettingResolver.Resolve(node.Node, node.ParameterSpecs, spec.Key);
            }
            else if (value is WiredParamValue)
            {
                EngineValue source = outputs[node.WiredParameterSourceSlots[spec.Key]];
                ValidateSource(spec.Key, source, type);
                values[spec.Key] = source;
            }
            else
            {
                values[spec.Key] = ResolveValue(value, type, node.Node.ParameterReference(spec.Key), outputs: null, execution);
            }
        }

        foreach (CompiledEngineInput input in node.EngineInputs)
        {
            if (input.SourceSlot >= 0)
            {
                EngineValue source = outputs[input.SourceSlot];

                if (input.Source is null)
                {
                    ValidateSource(input.Spec.Key, source, input.Spec.ValueType);
                }

                values[input.Spec.Key] = source;
            }
            else
            {
                values[input.Spec.Key] = input.Inline is { } inline
                    ? ResolveValue(inline, input.Spec.ValueType, node.Node.ParameterReference(input.Spec.Key), outputs: null, execution)
                    : input.Spec.DefaultValue ?? EngineValue.Unavailable(input.Spec.ValueType);
            }
        }

        return ResolvedEngineParameters.FromOwned(values, textSettings);
    }

    private static void ValidateSource(string key, EngineValue source, ValueType type)
    {
        if (source.ValueType != type)
        {
            throw new InvalidOperationException($"Parameter '{key}' requires {type}, got {source.ValueType}.");
        }
    }

    private static ResolvedEngineParameters Create(
        CompiledTradingNode node,
        IReadOnlyDictionary<PortReference, EngineValue> outputs,
        EngineExecutionInputs execution)
    {
        var values = new Dictionary<string, EngineValue>(StringComparer.Ordinal);
        var textSettings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ParamSpec spec in node.UnboundParameterSpecs)
        {
            ValueType type = node.Definition.ResolveParameterType(node.Node, spec);

            ParamValue value = node.Node.Parameters.GetValueOrDefault(spec.Key) ?? new FixedParamValue(Default(spec.DefaultValue));

            if (spec.ValueKind == ParamValueKind.Text)
            {
                textSettings[spec.Key] = NodeTextSettingResolver.Resolve(node.Node, node.ParameterSpecs, spec.Key);
            }
            else
            {
                values[spec.Key] = ResolveValue(value, type, node.Node.ParameterReference(spec.Key), outputs, execution);
            }
        }

        foreach (CompiledEngineInput input in node.EngineInputs)
        {
            values[input.Spec.Key] = input.Source is { } source
                ? outputs.GetValueOrDefault(source, EngineValue.Unavailable(input.Spec.ValueType))
                : input.Inline is { } inline
                    ? ResolveValue(inline, input.Spec.ValueType, node.Node.ParameterReference(input.Spec.Key), outputs, execution)
                    : input.Spec.DefaultValue ?? EngineValue.Unavailable(input.Spec.ValueType);
        }

        return ResolvedEngineParameters.FromOwned(values, textSettings);
    }

    private static bool DynamicValuesMatch(CompiledTradingNode node, IReadOnlyDictionary<PortReference, EngineValue> outputs, ResolvedEngineParameters cached)
    {
        for (var index = 0; index < node.UnboundParameterSpecs.Count; index++)
        {
            ParamSpec spec = node.UnboundParameterSpecs[index];

            if (node.Node.Parameters.GetValueOrDefault(spec.Key) is WiredParamValue wired &&
                (!cached.Values.TryGetValue(spec.Key, out EngineValue previous) ||
                    !outputs.GetValueOrDefault(wired.Source, EngineValue.Unavailable(previous.ValueType)).Equals(previous)))
            {
                return false;
            }
        }

        for (var index = 0; index < node.EngineInputs.Count; index++)
        {
            CompiledEngineInput input = node.EngineInputs[index];
            PortReference? source = input.Source ?? (input.Inline as WiredParamValue)?.Source;

            if (source is not null && !outputs.GetValueOrDefault(source, EngineValue.Unavailable(input.Spec.ValueType)).Equals(cached[input.Spec.Key]))
            {
                return false;
            }
        }

        return true;
    }

    public GraphValidationResult ValidateSelections(CompiledTradingGraph graph, EngineExecutionInputs execution)
    {
        var diagnostics = new List<GraphDiagnostic>();
        var active = new HashSet<PortReference>();

        foreach (CompiledTradingNode node in graph.Nodes)
        {
            foreach ((string key, ParamValue value) in node.Node.Parameters)
            {
                CompiledEngineInput? binding = node.EngineInputs.SingleOrDefault(input => input.Spec.Key == key);

                if (value is not (OptimizeParamValue or DecimalOptimizeParamValue) || binding?.Source is not null)
                {
                    continue;
                }

                PortReference reference = node.Node.ParameterReference(key);
                active.Add(reference);
                ValueType type = binding?.Spec.ValueType ?? (key == "period" ? ValueType.Integer : node.Node.DeclaredValueType ?? ValueType.Scalar);

                try
                {
                    ResolveValue(value, type, reference, new Dictionary<PortReference, EngineValue>(), execution);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArithmeticException or ArgumentException)
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.InvalidLiteralRange, exception.Message, node.Node.Id, ParamKey: key));
                }
            }
        }

        foreach (PortReference unknown in execution.OptimizedValues.Keys.Where(key => !active.Contains(key)))
        {
            diagnostics.Add(
                new GraphDiagnostic(GraphDiagnosticCode.UnknownParameter, "Optimization choice has no active range.", unknown.NodeId, ParamKey: unknown.PortKey));
        }

        return new GraphValidationResult(diagnostics.AsReadOnly());
    }

    private static EngineValue ResolveValue(
        ParamValue value,
        ValueType type,
        PortReference key,
        IReadOnlyDictionary<PortReference, EngineValue>? outputs,
        EngineExecutionInputs execution)
    {
        if (value is WiredParamValue wired)
        {
            EngineValue source = outputs!.GetValueOrDefault(wired.Source, EngineValue.Unavailable(type));

            if (source.ValueType != type)
            {
                throw new InvalidOperationException($"Parameter '{key.PortKey}' requires {type}, got {source.ValueType}.");
            }

            return source;
        }

        if (value is FixedParamValue fixedValue)
        {
            return EngineLiteralResolver.Resolve(fixedValue.Value, type) ?? throw new InvalidOperationException($"Invalid typed parameter '{key.PortKey}'.");
        }

        if (!execution.OptimizedValues.TryGetValue(key, out decimal selected))
        {
            throw new InvalidOperationException($"Missing exact optimization choice for '{key.PortKey}'.");
        }

        (decimal min, decimal max, decimal step) = value switch
        {
            DecimalOptimizeParamValue range => (range.Min, range.Max, range.Step),
            OptimizeParamValue range => (Legacy(range.Min, type), Legacy(range.Max, type), Legacy(range.Step, type)),
            _ => throw new InvalidOperationException("Unsupported parameter binding.")
        };

        if (selected < min || selected > max || !ExactDecimalGrid.IsOnGrid(selected, min, step))
        {
            throw new InvalidOperationException($"Optimization choice is outside the exact range for '{key.PortKey}'.");
        }

        return EngineValue.Numeric(type, selected);
    }

    private static decimal Legacy(double value, ValueType type)
    {
        return EngineLiteralResolver.Resolve(new NumberParamLiteral(value), type)?.Number
            ?? throw new InvalidOperationException("Legacy numeric parameter cannot be represented exactly.");
    }

    private static ParamLiteral Default(object value)
    {
        return value switch
        {
            string text => new TextParamLiteral(text),
            decimal number => new DecimalParamLiteral(number),
            double number => new NumberParamLiteral(number),
            int number => new DecimalParamLiteral(number),
            long number => new DecimalParamLiteral(number),
            bool boolean => new BooleanParamLiteral(boolean),
            _ => throw new InvalidOperationException("Unsupported schema default.")
        };
    }
}