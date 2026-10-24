using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Replay;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     The identity of what a compiled graph executes, under the engine's execution policy: node types and versions,
///     ranks and clocks, parameter schemas and values, bindings and output types. Labels, display names and display units
///     are left out, so editing them never changes the identity of a queued run. Decimals are exact text, so 1 and 1.0
///     are the same value.
/// </summary>
public static class GraphCompilationFingerprint
{
    private const string Version = "profitgraph-compilation/v1";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

    public static string Calculate(CompiledTradingGraph graph)
    {
        var metadata = new
        {
            Version,
            Policy = BacktestResult.ExecutionPolicyVersion,
            graph.WarmupCandles,
            graph.DeferredLifecycleRoutes,
            Nodes = graph.Nodes.Select(node => new
                {
                    node.Node.Id,
                    node.Node.TypeKey,
                    node.Node.SchemaVersion,
                    node.Node.DeclaredValueType,
                    node.Node.EvaluationMode,
                    node.DependencyRank,
                    node.Clock,
                    node.TradingBehavior,
                    node.EventField,
                    node.AllowedEvaluationModes,
                    Parameters = node.ParameterSpecs
                        .OrderBy(spec => spec.Key, StringComparer.Ordinal)
                        .Select(spec => new
                        {
                            spec.Key,
                            spec.ValueKind,
                            Default = Describe(spec.DefaultValue),
                            spec.MinValue,
                            spec.MaxValue,
                            spec.Step,
                            spec.AllowedValues
                        })
                        .ToArray(),
                    Values = node.Node.Parameters
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new { pair.Key, Value = Describe(pair.Value) })
                        .ToArray(),
                    Identities = node.Node.ParameterIdentities
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new { pair.Key, pair.Value })
                        .ToArray(),
                    Inputs = node.EngineInputs.Select(input => new
                        {
                            input.Spec.Key,
                            input.Spec.ValueType,
                            input.Spec.Required,
                            input.Spec.SupportsInline,
                            Default = input.Spec.DefaultValue is { } value ? Describe(value) : null,
                            input.Source,
                            Inline = input.Inline is null ? null : Describe(input.Inline)
                        })
                        .ToArray(),
                    node.Connections,
                    Outputs = node.OutputTypes
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new { pair.Key, pair.Value })
                        .ToArray()
                })
                .ToArray()
        };

        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(metadata, Options)));
    }

    private static string Describe(ParamValue value)
    {
        return value switch
        {
            FixedParamValue { Value: DecimalParamLiteral literal } => $"decimal:{ExactDecimalText.Format(literal.Value)}",
            FixedParamValue { Value: TextParamLiteral literal } => $"text:{literal.Value}",
            FixedParamValue { Value: BooleanParamLiteral literal } => $"boolean:{(literal.Value ? "true" : "false")}",
            FixedParamValue { Value: OrderIdParamLiteral literal } => $"orderId:{literal.Value.ToString(CultureInfo.InvariantCulture)}",
            FixedParamValue { Value: RejectionReasonParamLiteral literal } => $"rejectionReason:{literal.Value}",
            DecimalOptimizeParamValue optimize =>
                $"optimize:{ExactDecimalText.Format(optimize.Min)}:{ExactDecimalText.Format(optimize.Max)}:{ExactDecimalText.Format(optimize.Step)}",
            WiredParamValue wired => $"wired:{wired.Source.NodeId.Length.ToString(CultureInfo.InvariantCulture)}:{wired.Source.NodeId}:{wired.Source.PortKey}",
            _ => throw new InvalidOperationException($"Unsupported parameter value {value.GetType().Name}.")
        };
    }

    private static string? Describe(object? value)
    {
        return value switch
        {
            null => null,
            decimal number => $"decimal:{ExactDecimalText.Format(number)}",
            int number => $"decimal:{number.ToString(CultureInfo.InvariantCulture)}",
            long number => $"decimal:{number.ToString(CultureInfo.InvariantCulture)}",
            double number => $"double:{number.ToString("R", CultureInfo.InvariantCulture)}",
            bool flag => $"boolean:{(flag ? "true" : "false")}",
            string text => $"text:{text}",
            Enum member => $"{member.GetType().Name}:{member}",
            _ => throw new InvalidOperationException($"Unsupported parameter default {value.GetType().Name}.")
        };
    }

    private static string Describe(EngineValue value)
    {
        if (!value.IsAvailable)
        {
            return $"{value.ValueType}:unavailable";
        }

        string content = UnitAlgebra.IsNumeric(value.ValueType)
            ? ExactDecimalText.Format(value.Number)
            : value.ValueType switch
            {
                ValueType.Boolean => value.Boolean ? "true" : "false",
                ValueType.Tag => value.Tag,
                ValueType.OrderId => value.OrderId.ToString(CultureInfo.InvariantCulture),
                ValueType.OrderSide => value.Side.ToString(),
                ValueType.OrderType => value.Type.ToString(),
                ValueType.RejectionReason => value.Reason.ToString(),
                _ => throw new InvalidOperationException("Unsupported compiled inline default.")
            };

        return $"{value.ValueType}:{content}";
    }
}