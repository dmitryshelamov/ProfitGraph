using System.Collections.ObjectModel;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Экземпляр типа ноды с устойчивым ID и неизменяемыми настройками.
/// </summary>
public sealed record GraphNode
{
    public string Id { get; }
    public string TypeKey { get; }
    public int SchemaVersion { get; }
    public IReadOnlyDictionary<string, ParamValue> Parameters { get; }

    public IReadOnlyDictionary<string, PortReference> ParameterIdentities { get; }

    public ValueType? DeclaredValueType { get; }
    public ActionEvaluationMode? EvaluationMode { get; }
    public string? DisplayName { get; }

    public GraphNode(
        string id,
        string typeKey,
        int schemaVersion,
        IReadOnlyDictionary<string, ParamValue> parameters,
        ValueType? declaredValueType = null,
        ActionEvaluationMode? evaluationMode = null,
        IReadOnlyDictionary<string, PortReference>? parameterIdentities = null,
        string? displayName = null)
    {
        ParameterIdentities = new ReadOnlyDictionary<string, PortReference>(
            new Dictionary<string, PortReference>(parameterIdentities ?? new Dictionary<string, PortReference>(), StringComparer.Ordinal));
        DeclaredValueType = declaredValueType;
        EvaluationMode = evaluationMode;
        DisplayName = displayName;
        Id = id;
        TypeKey = typeKey;
        SchemaVersion = schemaVersion;
        Parameters = new ReadOnlyDictionary<string, ParamValue>(new Dictionary<string, ParamValue>(parameters, StringComparer.Ordinal));
    }

    public PortReference ParameterReference(string key)
    {
        return ParameterIdentities.GetValueOrDefault(key, new PortReference(Id, key));
    }
}