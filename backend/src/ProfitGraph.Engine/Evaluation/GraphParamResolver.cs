using System.Globalization;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Разрешает параметры для текущего бара и повторно проверяет динамические числовые значения.
/// </summary>
public sealed class GraphParamResolver
{
    public ResolvedParams? Resolve(CompiledNode compiled, IReadOnlyDictionary<PortReference, NodeOutput> outputs, GraphExecutionInputs execution)
    {
        var values = new Dictionary<string, ParamLiteral>(StringComparer.Ordinal);

        foreach (ParamSpec spec in compiled.Definition.Params)
        {
            ParamLiteral value;

            switch (compiled.Node.Parameters.GetValueOrDefault(spec.Key))
            {
                case null:
                    value = ResolveDefault(spec);

                    break;
                case FixedParamValue { Value: NumberParamLiteral number }:
                    value = number;

                    break;
                case FixedParamValue { Value: TextParamLiteral text }:
                    value = text;

                    break;
                case OptimizeParamValue optimize:
                    if (!execution.OptimizedValues.TryGetValue(new PortReference(compiled.Node.Id, spec.Key), out double selected) ||
                        selected < optimize.Min || selected > optimize.Max ||
                        !ParamConstraints.IsMultiple(selected - optimize.Min, optimize.Step))
                    {
                        throw new ParameterResolutionException(spec.Key, $"requires a valid Optimize value for node '{compiled.Node.Id}'.");
                    }

                    value = new NumberParamLiteral(selected);

                    break;
                case WiredParamValue wired:
                    if (!outputs.TryGetValue(wired.Source, out NodeOutput source) || source.Kind != PortKind.Series)
                    {
                        throw new ParameterResolutionException(spec.Key, "requires a resolved Series source.");
                    }

                    if (!source.IsAvailable)
                    {
                        return null;
                    }

                    value = new NumberParamLiteral(source.SeriesValue);

                    break;
                default:
                    throw new ParameterResolutionException(spec.Key, "has an unsupported binding.");
            }

            if ((spec.ValueKind == ParamValueKind.Number && value is not NumberParamLiteral) ||
                (spec.ValueKind == ParamValueKind.Text && value is not TextParamLiteral))
            {
                throw new ParameterResolutionException(spec.Key, $"must match the declared type '{spec.ValueKind}'.");
            }

            if (value is NumberParamLiteral numeric && !ParamConstraints.IsInRange(numeric.Value, spec))
            {
                throw new ParameterResolutionException(spec.Key, "is outside its declared range or step.");
            }

            if (value is TextParamLiteral textValue && (textValue.Value is null ||
                    (spec.AllowedValues is not null && !spec.AllowedValues.Contains(textValue.Value, StringComparer.Ordinal))))
            {
                throw new ParameterResolutionException(spec.Key, "must contain an allowed text value.");
            }

            values.Add(spec.Key, value);
        }

        return new ResolvedParams(values);
    }

    private static ParamLiteral ResolveDefault(ParamSpec spec)
    {
        return (spec.ValueKind, spec.DefaultValue) switch
        {
            (ParamValueKind.Number, sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal) =>
                new NumberParamLiteral(Convert.ToDouble(spec.DefaultValue, CultureInfo.InvariantCulture)),
            (ParamValueKind.Text, string text) => new TextParamLiteral(text),
            _ => throw new ParameterResolutionException(spec.Key, "has a default value that does not match its declared type.")
        };
    }
}