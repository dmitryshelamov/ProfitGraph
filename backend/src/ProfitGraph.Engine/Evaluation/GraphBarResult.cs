using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Снимок выходов всех нод после вычисления одного бара.
///     Нужен для проверки результатов и анализа вычислительного графа, без торговых результатов и PnL.
/// </summary>
public sealed record GraphBarResult(int BarIndex, IReadOnlyDictionary<PortReference, NodeOutput> Outputs);