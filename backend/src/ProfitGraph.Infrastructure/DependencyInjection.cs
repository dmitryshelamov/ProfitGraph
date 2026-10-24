using FluentValidation;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application;
using ProfitGraph.Infrastructure.Common.Messaging;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    ///     Registers the Application pipeline (validation outside the transaction, then the transaction) and PostgreSQL.
    /// </summary>
    public static IServiceCollection AddProfitGraphInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(mediatr =>
        {
            mediatr.RegisterServicesFromAssembly(typeof(ApplicationAssembly).Assembly);
            mediatr.AddOpenBehavior(typeof(ValidationBehavior<,>));
            mediatr.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });
        services.AddValidatorsFromAssembly(typeof(ApplicationAssembly).Assembly, includeInternalTypes: true);
        services.AddPostgres(configuration);

        return services;
    }
}