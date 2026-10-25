using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Common;

/// <summary>
///     Hosts the API in the Testing environment against this run's disposable database, applying migrations on startup.
///     The test kind of background work and the test history venues are registered next to the real ones, and a test may
///     add or replace services, such as a venue's mock transport.
/// </summary>
public sealed class ProfitGraphApiFactory(
    IReadOnlyDictionary<string, string>? settings = null,
    TimeProvider? clock = null,
    Action<IServiceCollection>? services = null) : WebApplicationFactory<Program>
{
    public ProfitGraphApiFactory(TimeProvider clock)
        : this(settings: null, clock)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", TestDatabase.ConnectionString);
        builder.UseSetting("Postgres:RunMigrationsOnStartup", "true");

        foreach ((string key, string value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(testServices =>
        {
            testServices.AddTestJob();
            testServices.AddTestHistory();

            if (clock is not null)
            {
                testServices.AddSingleton(clock);
            }

            services?.Invoke(testServices);
        });
    }
}