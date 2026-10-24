using System.Text.RegularExpressions;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces ADR 0003: only venue modules know concrete trading venues.
///     The core identifies venues through <c>VenueId</c> and module capabilities, never by name.
/// </summary>
public sealed partial class VenueNeutralityTests
{
    [Test]
    public void CoreSourcesDoNotNameConcreteVenues()
    {
        string[] offenders = RepositoryLayout.SourceFiles()
            .Where(path => !IsVenueModule(path))
            .Where(path => VenueName().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryLayout.SourceDirectory, path))
            .ToArray();

        offenders.ShouldBeEmpty("Move venue-specific code into a ProfitGraph.Venues.* module.");
    }

    [GeneratedRegex(@"\b(bybit|t-?invest|tinkoff|steam)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VenueName();

    private static bool IsVenueModule(string path)
    {
        string relative = Path.GetRelativePath(RepositoryLayout.SourceDirectory, path);

        return relative.StartsWith("ProfitGraph.Venues.", StringComparison.Ordinal);
    }
}