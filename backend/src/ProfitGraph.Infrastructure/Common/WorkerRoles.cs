using Microsoft.Extensions.Configuration;

namespace ProfitGraph.Infrastructure.Common;

/// <summary>
///     What a Worker process runs (stage 5 plan S5-17): <c>jobs</c> for background job lanes (ADR 0011), <c>live</c> for the
///     live bot runtime and outbox delivery, <c>scanner</c> for the market scanner (ADR 0059). One binary may run as one process
///     with <c>jobs</c> and <c>live</c>, the default, or as several; the scanner runs only where it is listed, so no default
///     process opens venue connections for it. Jobs assume a single process with the <c>jobs</c> role; live bots are fenced by
///     their leases; one scanner runs at a time under its lock.
/// </summary>
public static class WorkerRoles
{
    private const string SectionName = "Worker:Roles";
    public const string Jobs = "jobs";
    public const string Live = "live";
    public const string Scanner = "scanner";

    private static readonly string[] Default = [Jobs, Live];
    private static readonly string[] Known = [Jobs, Live, Scanner];

    /// <summary>
    ///     Reads the configured roles; none configured means <c>jobs</c> and <c>live</c>. An empty or unknown role stops startup.
    /// </summary>
    public static IReadOnlySet<string> Read(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SectionName);
        string[] configured = Default;

        if (section.Value is { } single)
        {
            configured = single.Split(separator: ',', StringSplitOptions.TrimEntries);
        }
        else if (section.Exists())
        {
            configured = [.. section.GetChildren().Select(child => child.Value?.Trim() ?? string.Empty)];
        }

        if (configured.Length == 0 || configured.Any(role => !Known.Contains(role, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException($"{SectionName} must list one or more of: {string.Join(", ", Known)}.");
        }

        return configured.ToHashSet(StringComparer.Ordinal);
    }
}