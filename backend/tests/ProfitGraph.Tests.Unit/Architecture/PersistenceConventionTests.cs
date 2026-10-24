using System.Text.RegularExpressions;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces the persistence layout from docs/architecture.md: migrations live in &lt;Feature&gt;/Migrations as
///     {version}_{snake_case}.cs with a *Migration class, and repository row models in Repositories/Contracts end with Db.
/// </summary>
public sealed partial class PersistenceConventionTests
{
    [Test]
    public void MigrationsAreNamedByVersionAndLiveInMigrationsFolders()
    {
        string[] offenders =
        [
            .. RepositoryLayout.SourceFiles()
                .Select(path => (Path: path, Match: MigrationAttribute().Match(File.ReadAllText(path))))
                .Where(file => file.Match.Success)
                .Where(file => !IsConventionalMigration(file.Path, file.Match.Groups["version"].Value))
                .Select(file => Path.GetRelativePath(RepositoryLayout.SourceDirectory, file.Path))
        ];

        offenders.ShouldBeEmpty("Name migrations {version}_{snake_case}.cs with a *Migration class in <Feature>/Migrations.");
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
        string folder = Path.GetFileName(Path.GetDirectoryName(path)!);
        string source = File.ReadAllText(path);

        return folder == "Migrations" && MigrationFileName().IsMatch(file) && file.StartsWith(version + "_", StringComparison.Ordinal) &&
            MigrationClass().IsMatch(source);
    }

    [GeneratedRegex(@"\[Migration\((?<version>\d+)\)\]")]
    private static partial Regex MigrationAttribute();

    [GeneratedRegex(@"^\d{12}_[a-z0-9]+(?:_[a-z0-9]+)*$")]
    private static partial Regex MigrationFileName();

    [GeneratedRegex(@"\bclass\s+\w+Migration\s*:\s*Migration\b")]
    private static partial Regex MigrationClass();
}