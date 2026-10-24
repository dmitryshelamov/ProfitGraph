namespace ProfitGraph.Domain.Optimization;

public sealed record WalkForwardWindow(int Index, int TrainStart, int TrainCount, int TestStart, int TestCount);