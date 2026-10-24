using FluentMigrator.Runner;
using FluentMigrator.Runner.Exceptions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ProfitGraph.Infrastructure.Common.Postgres;

public static class PostgresMigrations
{
    /// <summary>
    ///     Applies pending migrations. Hosts call it on startup when Postgres:RunMigrationsOnStartup is set, or for --migrate-only.
    /// </summary>
    public static void MigratePostgres(this IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();

        try
        {
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        }
        catch (MissingMigrationsException)
        {
            // FluentMigrator refuses an assembly without migrations; there is simply nothing to apply yet.
        }
    }

    public static bool ShouldMigrateOnStartup(this IServiceProvider services)
    {
        return services.GetRequiredService<IOptions<PostgresOptions>>().Value.RunMigrationsOnStartup;
    }
}