namespace ProfitGraph.Infrastructure.Common.Postgres;

internal sealed class PostgresOptions
{
    internal const string SectionName = "Postgres";
    internal const string ConnectionStringName = "Postgres";

    public int MaximumPoolSize { get; init; } = 20;
    public bool RunMigrationsOnStartup { get; init; }
}