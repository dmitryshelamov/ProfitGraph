namespace ProfitGraph.Domain.Optimization.WalkForward;

public sealed record WalkForwardWindow(int Index, int TrainStart, int TrainCount, int TestStart, int TestCount);