using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;

namespace ProfitGraph.Infrastructure.Common.Postgres;

internal static class PostgresRegistration
{
    internal static IServiceCollection AddPostgres(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString(PostgresOptions.ConnectionStringName)
            ?? throw new InvalidOperationException("PostgreSQL connection string 'Postgres' is not configured.");

        services.AddOptions<PostgresOptions>()
            .Bind(configuration.GetSection(PostgresOptions.SectionName))
            .Validate(options => options.MaximumPoolSize > 0, "Postgres:MaximumPoolSize must be greater than zero.")
            .Validate(options => IsValidConnectionString(connectionString, options.MaximumPoolSize), "PostgreSQL connection string or pool settings are invalid.")
            .ValidateOnStart();

        string ResolveConnectionString(IServiceProvider provider) => new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = provider.GetRequiredService<IOptions<PostgresOptions>>().Value.MaximumPoolSize
        }.ConnectionString;

        services.AddSingleton(provider => NpgsqlDataSource.Create(ResolveConnectionString(provider)));
        services.AddScoped(provider => provider.GetRequiredService<NpgsqlDataSource>().CreateConnection());
        services.AddScoped<PostgresTransactionAccessor>();
        services.AddScoped<PostgresTransactionExecutor>();
        services.AddScoped<ITransactionExecutor>(provider => provider.GetRequiredService<PostgresTransactionExecutor>());
        services.AddHealthChecks().AddCheck<PostgresHealthCheck>(PostgresHealthCheck.Name, tags: [HealthTags.Ready]);

        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(ResolveConnectionString)
                .ScanIn(typeof(PostgresRegistration).Assembly)
                .For.Migrations());

        return services;
    }

    private static bool IsValidConnectionString(string connectionString, int maximumPoolSize)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maximumPoolSize };

            return !string.IsNullOrWhiteSpace(builder.Host) && !string.IsNullOrWhiteSpace(builder.Database) && builder.MinPoolSize <= builder.MaxPoolSize;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}