using System.Numerics;

using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     A visited candidate: its position among visited candidates, its canonical grid index, the parameter values it
///     selects and its search stage (1, or 2 for a refinement).
/// </summary>
public sealed record OptimizationTrial(int Index, BigInteger GridIndex, EngineExecutionInputs Selections, int Stage = 1);