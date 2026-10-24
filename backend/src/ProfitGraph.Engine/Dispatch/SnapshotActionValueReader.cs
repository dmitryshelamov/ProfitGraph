using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Dispatch;

internal readonly struct SnapshotActionValueReader : IActionValueReader
{
    private readonly IReadOnlyDictionary<PortReference, EngineValue> _outputs;
    private readonly EngineExecutionInputs _execution;
    private readonly EngineParameterResolver _resolver;

    public SnapshotActionValueReader(
        IReadOnlyDictionary<PortReference, EngineValue> outputs,
        EngineExecutionInputs execution,
        EngineParameterResolver resolver)
    {
        _outputs = outputs;
        _execution = execution;
        _resolver = resolver;
    }

    public EngineValue ReadTrigger(CompiledInputBinding binding)
    {
        return _outputs[binding.Source];
    }

    public ResolvedEngineParameters ResolveParameters(CompiledTradingNode node)
    {
        return _resolver.Resolve(node, _outputs, _execution);
    }
}