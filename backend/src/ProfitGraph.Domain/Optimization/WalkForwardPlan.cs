namespace ProfitGraph.Domain.Optimization;

public sealed class WalkForwardPlan
{
    public IReadOnlyList<WalkForwardWindow> Windows { get; }

    private WalkForwardPlan(WalkForwardWindow[] windows)
    {
        Windows = Array.AsReadOnly(windows);
    }

    public static WalkForwardPlan Create(int inSampleBars, WalkForwardSettings settings)
    {
        if (settings is null)
        {
            throw new WalkForwardWindowException("Walk-forward settings are required.");
        }

        int divisor = checked(settings.TrainToTestRatio + settings.Windows);
        int testCount = inSampleBars / divisor;

        if (testCount < 1)
        {
            throw new WalkForwardWindowException(inSampleBars);
        }

        int offset = checked(inSampleBars - divisor * testCount);
        var windows = new WalkForwardWindow[settings.Windows];

        for (var index = 0; index < windows.Length; index++)
        {
            int trainStart = settings.Scheme == WalkForwardScheme.Rolling ? checked(offset + index * testCount) : 0;
            int trainCount = settings.Scheme == WalkForwardScheme.Rolling
                ? checked(settings.TrainToTestRatio * testCount)
                : checked(offset + (settings.TrainToTestRatio + index) * testCount);

            if (trainCount < 1)
            {
                throw new WalkForwardWindowException(inSampleBars);
            }

            windows[index] = new WalkForwardWindow(index, trainStart, trainCount, checked(trainStart + trainCount), testCount);
        }

        return new WalkForwardPlan(windows);
    }
}