using System.Text.RegularExpressions;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces the persistence layout from docs/architecture.md: migrations live in Infrastructure/Common/Postgres/Migrations,
///     or in Persistence/Migrations of the venue module that owns their tables (ADR 0022, C8), as {version}_{snake_case}.cs
///     with a *Migration class, and repository row models in Repositories/Contracts end with Db.
/// </summary>
public sealed partial class PersistenceConventionTests
{
    private const string MigrationFolder = "ProfitGraph.Infrastructure/Common/Postgres/Migrations";

    [Test]
    public void MigrationsAreNamedByVersionAndLiveInOneFolder()
    {
        string[] offenders =
        [
            .. RepositoryLayout.SourceFiles()
                .Select(path => (Path: path, Match: MigrationAttribute().Match(File.ReadAllText(path))))
                .Where(file => file.Match.Success)
                .Where(file => !IsConventionalMigration(file.Path, file.Match.Groups["version"].Value))
                .Select(file => Path.GetRelativePath(RepositoryLayout.SourceDirectory, file.Path))
        ];

        offenders.ShouldBeEmpty($"Name migrations {{version}}_{{snake_case}}.cs with a *Migration class in {MigrationFolder}.");
    }

    [Test]
    public void RepositoryContractsAreDbModels()
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        string[] offenders =
        [
            .. RepositoryLayout.SourceFiles()
                .Where(path => path.Contains($"{separator}Repositories{separator}Contracts{separator}", StringComparison.Ordinal))
                .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("Db", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(RepositoryLayout.SourceDirectory, path))
        ];

        offenders.ShouldBeEmpty("Repository row models are named *Db.");
    }

    private static bool IsConventionalMigration(string path, string version)
    {
        string file = Path.GetFileNameWithoutExtension(path);
        string folder = Path.GetRelativePath(RepositoryLayout.SourceDirectory, Path.GetDirectoryName(path)!).Replace(Path.DirectorySeparatorChar, newChar: '/');
        string source = File.ReadAllText(path);

        return (folder == MigrationFolder || VenueMigrationFolder().IsMatch(folder)) && MigrationFileName().IsMatch(file) &&
            file.StartsWith(version + "_", StringComparison.Ordinal) &&
            MigrationClass().IsMatch(source);
    }

    [GeneratedRegex(@"\[Migration\((?<version>\d+)\)\]")]
    private static partial Regex MigrationAttribute();

    [GeneratedRegex(@"^ProfitGraph\.Venues\.\w+/Persistence/Migrations$")]
    private static partial Regex VenueMigrationFolder();

    [GeneratedRegex(@"^\d{12}_[a-z0-9]+(?:_[a-z0-9]+)*$")]
    private static partial Regex MigrationFileName();

    [GeneratedRegex(@"\bclass\s+\w+Migration\s*:\s*Migration\b")]
    private static partial Regex MigrationClass();
}