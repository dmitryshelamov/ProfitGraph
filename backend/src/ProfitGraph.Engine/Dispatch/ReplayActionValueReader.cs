using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Dispatch;

internal readonly struct ReplayActionValueReader : IActionValueReader
{
    private readonly EngineValue[] _outputs;
    private readonly CandleValueEvaluator _evaluator;

    public ReplayActionValueReader(EngineValue[] outputs, CandleValueEvaluator evaluator)
    {
        _outputs = outputs;
        _evaluator = evaluator;
    }

    public EngineValue ReadTrigger(CompiledInputBinding binding)
    {
        return _evaluator.ReadReplayValue(binding.SourceSlot, _outputs);
    }

    public ResolvedEngineParameters ResolveParameters(CompiledTradingNode node)
    {
        return _evaluator.ResolveReplayParameters(node, _outputs);
    }
}