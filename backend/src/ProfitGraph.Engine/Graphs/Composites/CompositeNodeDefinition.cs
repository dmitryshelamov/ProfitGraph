using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Graphs.Composites;

public sealed class CompositeNodeDefinition : IExpandableNodeDefinition
{
    public StrategyGraph Graph { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<PortReference>> InputMappings { get; }
    public IReadOnlyDictionary<string, PortReference> OutputMappings { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<PortReference>> ParameterMappings { get; }
    public string TypeKey { get; }
    public NodeCategory Category => NodeCategory.Composite;
    public int SchemaVersion { get; }
    public IReadOnlyList<PortSpec> Inputs { get; }
    public IReadOnlyList<PortSpec> Outputs { get; }
    public IReadOnlyList<ParamSpec> Params { get; }

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; }

    public CompositeNodeDefinition(
        string typeKey,
        int schemaVersion,
        StrategyGraph graph,
        IReadOnlyList<PortSpec> inputs,
        IReadOnlyList<PortSpec> outputs,
        IReadOnlyList<ParamSpec> parameters,
        IReadOnlyList<EngineInputSpec> engineInputs,
        IReadOnlyDictionary<string, IReadOnlyList<PortReference>> inputMappings,
        IReadOnlyDictionary<string, PortReference> outputMappings,
        IReadOnlyDictionary<string, IReadOnlyList<PortReference>> parameterMappings)
    {
        TypeKey = typeKey;
        SchemaVersion = schemaVersion;
        Graph = graph;
        Inputs = inputs;
        Outputs = outputs;
        Params = parameters;
        EngineInputs = engineInputs;
        InputMappings = inputMappings;
        OutputMappings = outputMappings;
        ParameterMappings = parameterMappings;
    }
}