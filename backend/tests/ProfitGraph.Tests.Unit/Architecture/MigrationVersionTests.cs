using System.Globalization;
using System.Reflection;

using FluentMigrator;

using ProfitGraph.Infrastructure;
using ProfitGraph.Venues.Steam;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces ADR 0006: migration versions use yyyyMMddNNNN and never collide across Infrastructure and the venue modules
///     that own tables (ADR 0022, C8), so parallel branches fail in CI instead of at startup.
/// </summary>
public sealed class MigrationVersionTests
{
    [Test]
    public void MigrationVersionsAreUniqueAndDateBased()
    {
        (string Type, long Version)[] migrations = new[] { typeof(DependencyInjection).Assembly, typeof(SteamRegistration).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => (Type: type.FullName!, Attribute: type.GetCustomAttribute<MigrationAttribute>()))
            .Where(migration => migration.Attribute is not null)
            .Select(migration => (migration.Type, migration.Attribute!.Version))
            .ToArray();

        Problems(migrations).ShouldBeEmpty();
    }

    [Test]
    public void DuplicateAndMalformedVersionsAreReported()
    {
        (string Type, long Version)[] migrations = [("A", 202610240001), ("B", 202610240001), ("C", 20261024001), ("D", 202613400001)];

        Problems(migrations).Length.ShouldBe(3);
    }

    private static string[] Problems(IReadOnlyCollection<(string Type, long Version)> migrations)
    {
        IEnumerable<string> duplicates = migrations
            .GroupBy(migration => migration.Version)
            .Where(group => group.Count() > 1)
            .Select(group => $"Duplicate version {group.Key}: {string.Join(", ", group.Select(migration => migration.Type))}.");
        IEnumerable<string> malformed = migrations
            .Where(migration => !IsDateBased(migration.Version))
            .Select(migration => $"{migration.Type} version {migration.Version} is not yyyyMMddNNNN.");

        return duplicates.Concat(malformed).ToArray();
    }

    private static bool IsDateBased(long version)
    {
        var text = version.ToString(CultureInfo.InvariantCulture);

        return text.Length == 12 && DateOnly.TryParseExact(text[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }
}