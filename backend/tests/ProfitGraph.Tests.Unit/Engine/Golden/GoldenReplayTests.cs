namespace ProfitGraph.Tests.Unit.Engine.Golden;

/// <summary>
///     Compares every golden scenario with the canonical result recorded from the archived Profit Fox Designer engine
///     (mvp@f26ab20d). A difference is a behavior change and must be deliberate and explained in the pull request.
/// </summary>
public sealed class GoldenReplayTests
{
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static IEnumerable<TestCaseData> Scenarios()
    {
        return GoldenReplay.Templates.SelectMany(template => GoldenReplay.Datasets.Select(dataset => new TestCaseData(template, dataset)));
    }

    [TestCaseSource(nameof(Scenarios))]
    public void ReplayMatchesArchivedEngine(string template, string dataset)
    {
        string expected = File.ReadAllText(Path.Combine(FixtureDirectory, "Golden", $"{template}.{dataset}.txt"));

        string actual = GoldenReplay.Run(template, dataset, Path.Combine(FixtureDirectory, "MarketData"));

        actual.ShouldBe(expected);
    }
}