using System.Text.RegularExpressions;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces ADR 0003: only venue modules know concrete trading venues.
///     The core identifies venues through <c>VenueId</c> and module capabilities, never by name. A composition root
///     may name a module only to register it: its <c>using</c> and its <c>Add...Venue()</c> call.
/// </summary>
public sealed partial class VenueNeutralityTests
{
    [Test]
    public void CoreSourcesDoNotNameConcreteVenues()
    {
        string[] offenders = RepositoryLayout.SourceFiles()
            .Where(path => !IsVenueModule(path))
            .Where(path => VenueName().IsMatch(CoreText(path)))
            .Select(path => Path.GetRelativePath(RepositoryLayout.SourceDirectory, path))
            .ToArray();

        offenders.ShouldBeEmpty("Move venue-specific code into a ProfitGraph.Venues.* module.");
    }

    [GeneratedRegex(@"\b(bybit|t-?invest|tinkoff|steam)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VenueName();

    [GeneratedRegex(@"^\s*(using ProfitGraph\.Venues\.\w+;|builder\.Services\.Add\w+Venue\(\);)\s*$")]
    private static partial Regex ModuleRegistration();

    private static string CoreText(string path)
    {
        string relative = Path.GetRelativePath(RepositoryLayout.SourceDirectory, path);
        bool compositionRoot = relative.StartsWith("ProfitGraph.Api", StringComparison.Ordinal) || relative.StartsWith("ProfitGraph.Worker", StringComparison.Ordinal);

        return string.Join(separator: '\n', File.ReadAllLines(path).Where(line => !(compositionRoot && ModuleRegistration().IsMatch(line))));
    }

    private static bool IsVenueModule(string path)
    {
        string relative = Path.GetRelativePath(RepositoryLayout.SourceDirectory, path);

        return relative.StartsWith("ProfitGraph.Venues.", StringComparison.Ordinal);
    }
}