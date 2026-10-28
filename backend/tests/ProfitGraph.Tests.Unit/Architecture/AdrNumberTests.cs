using System.Text.RegularExpressions;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces the ADR numbering from AGENTS.md: every file in docs/adr is NNNN-kebab-case.md with a number no other ADR has,
///     so a reference such as "ADR 0027" names one decision.
/// </summary>
public sealed partial class AdrNumberTests
{
    [Test]
    public void AdrNumbersAreUnique()
    {
        string[] files = [.. Directory.GetFiles(RepositoryLayout.AdrDirectory, "*.md").Select(path => Path.GetFileName(path))];

        Problems(files).ShouldBeEmpty("Give each ADR the next free number: the highest existing number + 1.");
    }

    [Test]
    public void DuplicateAndMalformedNumbersAreReported()
    {
        string[] files = ["0001-first.md", "0002-second.md", "0002-other.md", "03-short.md", "0004_underscore.md"];

        Problems(files).Length.ShouldBe(3);
    }

    private static string[] Problems(IReadOnlyCollection<string> files)
    {
        IEnumerable<string> duplicates = files
            .Where(file => AdrFileName().IsMatch(file))
            .GroupBy(file => file[..4])
            .Where(group => group.Count() > 1)
            .Select(group => $"ADR {group.Key} is shared by {string.Join(", ", group.Order(StringComparer.Ordinal))}.");
        IEnumerable<string> malformed = files
            .Where(file => !AdrFileName().IsMatch(file))
            .Select(file => $"{file} is not NNNN-kebab-case.md.");

        return duplicates.Concat(malformed).ToArray();
    }

    [GeneratedRegex(@"^\d{4}-[a-z0-9]+(?:-[a-z0-9]+)*\.md$")]
    private static partial Regex AdrFileName();
}