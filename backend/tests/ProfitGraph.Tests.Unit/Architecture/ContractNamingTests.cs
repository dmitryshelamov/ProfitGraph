namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces contract naming from docs/architecture.md: Application contracts are *Internal, HTTP contracts are *Request
///     or *Response, and every type in a Converters folder is a *Converter.
/// </summary>
public sealed class ContractNamingTests
{
    [Test]
    public void ApplicationContractsAreInternalModels()
    {
        Offenders("ProfitGraph.Application", "Contracts", name => name.EndsWith("Internal", StringComparison.Ordinal))
            .ShouldBeEmpty("Application contracts are named *Internal.");
    }

    [Test]
    public void HttpContractsAreRequestsOrResponses()
    {
        Offenders("ProfitGraph.Api", "Contracts", name => name.EndsWith("Request", StringComparison.Ordinal) || name.EndsWith("Response", StringComparison.Ordinal))
            .ShouldBeEmpty("HTTP contracts are named *Request or *Response.");
    }

    [Test]
    public void ConvertersAreNamedAsConverters()
    {
        string[] offenders =
        [
            .. new[] { "ProfitGraph.Application", "ProfitGraph.Api", "ProfitGraph.Infrastructure" }
                .SelectMany(project => Offenders(project, "Converters", name => name.EndsWith("Converter", StringComparison.Ordinal)))
        ];

        offenders.ShouldBeEmpty("Types in Converters folders are named *Converter.");
    }

    private static string[] Offenders(string project, string folder, Func<string, bool> isConventional)
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        string root = Path.Combine(RepositoryLayout.SourceDirectory, project) + separator;

        return
        [
            .. RepositoryLayout.SourceFiles()
                .Where(path => path.StartsWith(root, StringComparison.Ordinal) && path.Contains($"{separator}{folder}{separator}", StringComparison.Ordinal))
                .Where(path => !isConventional(Path.GetFileNameWithoutExtension(path)))
                .Select(path => Path.GetRelativePath(RepositoryLayout.SourceDirectory, path))
        ];
    }
}