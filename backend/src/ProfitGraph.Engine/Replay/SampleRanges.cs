namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Индексы in-sample и out-of-sample баров внутри одного набора свечей; хвост всегда идёт после in-sample части.
/// </summary>
public sealed record SampleRanges(int InSampleBars, int OutOfSampleBars)
{
    public int TotalBars => InSampleBars + OutOfSampleBars;
    public Range InSample => new Range(Index.Start, InSampleBars);
    public Range OutOfSample => new Range(InSampleBars, TotalBars);
}