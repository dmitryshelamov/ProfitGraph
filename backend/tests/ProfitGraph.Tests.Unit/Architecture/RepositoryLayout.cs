namespace ProfitGraph.Tests.Unit.Architecture;

internal static class RepositoryLayout
{
    private static readonly Lazy<string> Root = new Lazy<string>(FindRoot);

    public static string SourceDirectory => Path.Combine(Root.Value, "backend", "src");

    public static IEnumerable<string> SourceProjects()
    {
        return Directory.GetFiles(SourceDirectory, "ProfitGraph.*.csproj", SearchOption.AllDirectories);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "ProfitGraph.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}