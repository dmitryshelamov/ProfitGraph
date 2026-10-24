namespace ProfitGraph.Domain.Optimization;

public sealed record WalkForwardSettings
{
    public const int DefaultWindows = 6;
    public const int MinimumWindows = 2;
    public const int MaximumWindows = 50;
    public const int DefaultTrainToTestRatio = 4;
    public const int MinimumTrainToTestRatio = 1;
    public const int MaximumTrainToTestRatio = 20;

    public int Windows { get; }
    public int TrainToTestRatio { get; }
    public WalkForwardScheme Scheme { get; }

    public WalkForwardSettings(
        int windows = DefaultWindows,
        int trainToTestRatio = DefaultTrainToTestRatio,
        WalkForwardScheme scheme = WalkForwardScheme.Rolling)
    {
        if (windows is < MinimumWindows or > MaximumWindows)
        {
            throw new WalkForwardWindowException($"Windows must be between {MinimumWindows} and {MaximumWindows}.");
        }

        if (trainToTestRatio is < MinimumTrainToTestRatio or > MaximumTrainToTestRatio)
        {
            throw new WalkForwardWindowException($"Train-to-test ratio must be between {MinimumTrainToTestRatio} and {MaximumTrainToTestRatio}.");
        }

        if (!Enum.IsDefined(scheme))
        {
            throw new WalkForwardWindowException("Walk-forward scheme must be rolling or anchored.");
        }

        Windows = windows;
        TrainToTestRatio = trainToTestRatio;
        Scheme = scheme;
    }
}