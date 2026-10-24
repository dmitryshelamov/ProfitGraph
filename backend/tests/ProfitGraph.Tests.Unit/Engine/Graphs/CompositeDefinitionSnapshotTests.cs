using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Composites;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class CompositeDefinitionSnapshotTests
{
    [TestCase("HasBaseBalance")]
    [TestCase("NoActiveOrders")]
    [TestCase("NoActiveTaggedOrder")]
    [TestCase("PositionFlat")]
    [TestCase("PositionFlatPreview")]
    public void Definition_MatchesVersionedCanonicalJson(string typeKey)
    {
        CompositeNodeDefinition definition = BuiltInComposites.Definitions.Single(item => item.TypeKey == typeKey);
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Snapshots", "Composites", $"{typeKey}.v{definition.SchemaVersion}.json");
        File.Exists(path).ShouldBeTrue($"A schema version change requires a new snapshot: {path}");
        File.ReadAllText(path).TrimEnd().ShouldBe(Snapshot(definition));
    }

    private static string Snapshot(CompositeNodeDefinition definition)
    {
        var snapshot = new
        {
            typeKey = definition.TypeKey,
            schemaVersion = definition.SchemaVersion,
            inputs = definition.Inputs.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new { item.Key, kind = item.Kind.ToString(), valueType = item.ValueType.ToString(), item.Label }),
            outputs = definition.Outputs.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new { item.Key, kind = item.Kind.ToString(), valueType = item.ValueType.ToString(), item.Label }),
            parameters = definition.Params.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new
                {
                    item.Key,
                    valueKind = item.ValueKind.ToString(),
                    defaultValue = Convert.ToString(item.DefaultValue, CultureInfo.InvariantCulture),
                    item.Label
                }),
            engineInputs = definition.EngineInputs.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new
                {
                    item.Key,
                    valueType = item.ValueType.ToString(),
                    item.Required,
                    item.SupportsInline,
                    defaultValue = item.DefaultValue?.Number.ToString(CultureInfo.InvariantCulture)
                }),
            graph = new
            {
                nodes = definition.Graph.Nodes.OrderBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => new
                    {
                        item.Id,
                        item.TypeKey,
                        item.SchemaVersion,
                        declaredValueType = item.DeclaredValueType?.ToString(),
                        evaluationMode = item.EvaluationMode?.ToString(),
                        parameters = item.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => new { key = pair.Key, value = Parameter(pair.Value) })
                    }),
                connections = definition.Graph.Connections.Select(item => new { source = Reference(item.Source), target = Reference(item.Target) })
                    .OrderBy(item => item.source, StringComparer.Ordinal)
                    .ThenBy(item => item.target, StringComparer.Ordinal)
            },
            inputMappings = definition.InputMappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { key = pair.Key, targets = pair.Value.Select(Reference).OrderBy(item => item, StringComparer.Ordinal) }),
            outputMappings = definition.OutputMappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { key = pair.Key, source = Reference(pair.Value) }),
            parameterMappings = definition.ParameterMappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { key = pair.Key, targets = pair.Value.Select(Reference).OrderBy(item => item, StringComparer.Ordinal) })
        };

        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string Reference(PortReference reference)
    {
        return $"{reference.NodeId}.{reference.PortKey}";
    }

    private static string Parameter(ParamValue value)
    {
        return value switch
        {
            FixedParamValue { Value: DecimalParamLiteral literal } => $"decimal:{literal.Value.ToString(CultureInfo.InvariantCulture)}",
            FixedParamValue { Value: TextParamLiteral literal } => $"text:{literal.Value}",
            FixedParamValue { Value: RejectionReasonParamLiteral literal } => $"reason:{literal.Value}",
            WiredParamValue wired => $"wired:{Reference(wired.Source)}",
            _ => throw new NotSupportedException($"Snapshot format does not support {value.GetType().Name}.")
        };
    }
}