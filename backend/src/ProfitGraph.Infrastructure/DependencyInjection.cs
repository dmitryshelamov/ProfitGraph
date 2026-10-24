using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application;
using ProfitGraph.Infrastructure.Common.Messaging;

namespace ProfitGraph.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    ///     Registers the Application pipeline: handlers, validators and MediatR behaviors in execution order.
    /// </summary>
    public static IServiceCollection AddProfitGraphInfrastructure(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(ApplicationAssembly).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(typeof(ApplicationAssembly).Assembly, includeInternalTypes: true);

        return services;
    }
}