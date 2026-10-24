using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api;

namespace ProfitGraph.Tests.Integration.Common;

/// <summary>
///     Hosts the API in the Testing environment against this run's disposable database, applying migrations on startup.
/// </summary>
public sealed class ProfitGraphApiFactory(IReadOnlyDictionary<string, string>? settings = null, TimeProvider? clock = null) : WebApplicationFactory<Program>
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

        if (clock is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(clock));
        }
    }
}