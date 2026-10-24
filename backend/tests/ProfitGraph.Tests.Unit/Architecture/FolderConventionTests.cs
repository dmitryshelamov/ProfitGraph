using System.Text.RegularExpressions;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces the folder conventions from docs/architecture.md: feature folders first, and namespaces that follow folders.
/// </summary>
public sealed partial class FolderConventionTests
{
    private static readonly string[] FeatureFirstProjects =
    [
        "ProfitGraph.Domain",
        "ProfitGraph.Application",
        "ProfitGraph.Infrastructure",
        "ProfitGraph.Api"
    ];

    private static readonly string[] TechnicalTypeFolders =
    [
        "Commands",
        "Queries",
        "Contracts",
        "Converters",
        "Validators",
        "Services",
        "Entities",
        "ValueObjects",
        "Repositories",
        "Exceptions",
        "Events",
        "Controllers",
        "Handlers",
        "Models"
    ];

    [Test]
    public void TopLevelFoldersAreFeaturesNotTechnicalTypes()
    {
        string[] offenders = FeatureFirstProjects
            .SelectMany(project => Directory.GetDirectories(Path.Combine(RepositoryLayout.SourceDirectory, project))
                .Select(directory => $"{project}/{Path.GetFileName(directory)}"))
            .Where(folder => TechnicalTypeFolders.Contains(folder[(folder.IndexOf('/') + 1)..]))
            .ToArray();

        offenders.ShouldBeEmpty("Group code by feature first; see Folder Conventions in docs/architecture.md.");
    }

    [Test]
    public void NamespacesFollowFolders()
    {
        string[] offenders = RepositoryLayout.SourceFiles()
            .Where(path => DeclaredNamespace(path) is { } declared && declared != ExpectedNamespace(path))
            .Select(path => Path.GetRelativePath(RepositoryLayout.SourceDirectory, path))
            .ToArray();

        offenders.ShouldBeEmpty("A namespace must equal the project name followed by the folder path.");
    }

    [GeneratedRegex(@"^namespace\s+([\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespace();

    private static string? DeclaredNamespace(string path)
    {
        Match match = FileScopedNamespace().Match(File.ReadAllText(path));

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ExpectedNamespace(string path)
    {
        string relative = Path.GetRelativePath(RepositoryLayout.SourceDirectory, Path.GetDirectoryName(path)!);

        return relative.Replace(Path.DirectorySeparatorChar, newChar: '.');
    }
}