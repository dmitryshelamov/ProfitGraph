using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Infrastructure;
using ProfitGraph.Infrastructure.Jobs;

namespace ProfitGraph.Tests.Integration.Jobs;

/// <summary>
///     The Worker's job runner, hosted in the test process against this run's database with the test kind of work.
///     Start the API factory first: it applies the migrations.
/// </summary>
public static class WorkerHost
{
    public static IHost Create(TimeSpan? pollingInterval = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = TestDatabase.ConnectionString,
                ["BackgroundJobs:PollingInterval"] = (pollingInterval ?? TimeSpan.FromMilliseconds(50)).ToString()
            });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProfitGraphInfrastructure(builder.Configuration);
        builder.Services.AddBackgroundJobRunner();
        builder.Services.AddTestJob();

        return builder.Build();
    }

    public static IServiceCollection AddTestJob(this IServiceCollection services)
    {
        services.AddBackgroundJob<TestJobRequest, TestJobResult>();
        services.AddTransient<IRequestHandler<TestJobRequest, TestJobResult>, TestJobHandler>();

        return services;
    }
}