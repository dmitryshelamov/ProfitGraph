using FluentValidation;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application;
using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Strategies.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Accounts.Repositories;
using ProfitGraph.Infrastructure.Accounts.Security;
using ProfitGraph.Infrastructure.Common.Messaging;
using ProfitGraph.Infrastructure.Common.NodeTypes;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.MarketData;
using ProfitGraph.Infrastructure.Strategies.Repositories;
using ProfitGraph.Infrastructure.Strategies.Repositories.Converters;

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
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        services.AddScoped<IStrategyVersionReader, StrategyVersionReader>();
        services.AddSingleton<StrategySnapshotDbConverter>();
        services.AddNodeTypes();
        services.AddMarketData(configuration);

        return services;
    }
}