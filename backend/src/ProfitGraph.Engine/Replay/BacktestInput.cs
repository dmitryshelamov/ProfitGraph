using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Frozen validated replay inputs; history and selection errors are rejected before any account mutation.
/// </summary>
public sealed class BacktestInput
{
    public CompiledTradingGraph Graph { get; }
    public EngineExecutionInputs Selections { get; }
    public ExecutionSettings Settings { get; }
    public InstrumentRules Rules { get; }
    public Timeframe Timeframe { get; }
    public IReadOnlyList<Candle> Warmup { get; }
    public IReadOnlyList<Candle> Replay { get; }

    /// <summary>
    ///     The market closures of the history (ADR 0023, TC1); position age leaves them out.
    /// </summary>
    public IReadOnlyList<MarketClosure> Closures { get; }

    public SampleRanges SampleRanges { get; }

    public BacktestInput(
        CompiledTradingGraph graph,
        EngineExecutionInputs selections,
        ExecutionSettings settings,
        InstrumentRules rules,
        Timeframe timeframe,
        IEnumerable<Candle> warmup,
        IEnumerable<Candle> replay,
        OutOfSampleSplit? split = null)
        : this(graph, selections, settings, rules, new PreparedReplay(timeframe, warmup, replay), split)
    {
    }

    public BacktestInput(
        CompiledTradingGraph graph,
        EngineExecutionInputs selections,
        ExecutionSettings settings,
        InstrumentRules rules,
        PreparedReplay preparedReplay,
        OutOfSampleSplit? split = null)
    {
        Graph = graph;
        Selections = new EngineExecutionInputs(selections.OptimizedValues);
        Settings = settings;
        Rules = rules;
        Timeframe = preparedReplay.Timeframe;
        Warmup = preparedReplay.Warmup;
        Replay = preparedReplay.Replay;
        Closures = preparedReplay.Closures;

        GraphValidationResult validation = new EngineParameterResolver().ValidateSelections(graph, Selections);

        if (!validation.IsValid)
        {
            throw new BacktestInputException(validation);
        }

        SampleRanges = (split ?? OutOfSampleSplit.None).Split(Replay.Count);
    }
}