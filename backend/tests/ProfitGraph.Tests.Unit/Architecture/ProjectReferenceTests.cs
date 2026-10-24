using System.Xml.Linq;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Enforces the inward dependency direction from ADR 0002:
///     Api/Worker -> Infrastructure, Venues.* -> Application -> Domain -> Engine.
/// </summary>
public sealed class ProjectReferenceTests
{
    private static readonly Dictionary<string, string[]> AllowedReferences = new Dictionary<string, string[]>
    {
        ["ProfitGraph.Engine"] = [],
        ["ProfitGraph.Domain"] = ["ProfitGraph.Engine"],
        ["ProfitGraph.Application"] = ["ProfitGraph.Domain"],
        ["ProfitGraph.Infrastructure"] = ["ProfitGraph.Application"],
        ["ProfitGraph.Api"] = ["ProfitGraph.Infrastructure"],
        ["ProfitGraph.Worker"] = ["ProfitGraph.Infrastructure"]
    };

    private static readonly Dictionary<string, string[]> AllowedPackages = new Dictionary<string, string[]>
    {
        ["ProfitGraph.Engine"] = [],
        ["ProfitGraph.Domain"] = ["MediatR.Contracts"]
    };

    [Test]
    public void EverySourceProjectHasDeclaredDependencyRules()
    {
        string[] undeclared = RepositoryLayout.SourceProjects()
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !AllowedReferences.ContainsKey(name!) && !IsVenueModule(name!))
            .Select(name => name!)
            .ToArray();

        undeclared.ShouldBeEmpty("Add new projects to the dependency rules in this test and to docs/architecture.md.");
    }

    [Test]
    public void ProjectReferencesPointInward()
    {
        foreach (string project in RepositoryLayout.SourceProjects())
        {
            string name = Path.GetFileNameWithoutExtension(project);
            string[] references = ProjectReferences(project);
            string[] allowed = IsVenueModule(name) ? ["ProfitGraph.Application"] : AllowedReferences.GetValueOrDefault(name, []);
            string[] forbidden = references
                .Where(reference => !allowed.Contains(reference) && !(IsCompositionRoot(name) && IsVenueModule(reference)))
                .ToArray();

            forbidden.ShouldBeEmpty($"{name} has forbidden project references.");
        }
    }

    [Test]
    public void CoreProjectsUseOnlyAllowedPackages()
    {
        foreach (string project in RepositoryLayout.SourceProjects())
        {
            string name = Path.GetFileNameWithoutExtension(project);

            if (!AllowedPackages.TryGetValue(name, out string[]? allowed))
            {
                continue;
            }

            string[] forbidden = XDocument.Load(project)
                .Descendants("PackageReference")
                .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
                .Where(package => !allowed.Contains(package))
                .ToArray();

            forbidden.ShouldBeEmpty($"{name} must stay free of infrastructure packages.");
        }
    }

    private static string[] ProjectReferences(string project)
    {
        return XDocument.Load(project)
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Replace(oldChar: '\\', newChar: '/')))
            .ToArray();
    }

    private static bool IsVenueModule(string name)
    {
        return name.StartsWith("ProfitGraph.Venues.", StringComparison.Ordinal);
    }

    private static bool IsCompositionRoot(string name)
    {
        return name is "ProfitGraph.Api" or "ProfitGraph.Worker";
    }
}