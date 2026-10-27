using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Infrastructure;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Infrastructure against this run's database without hosted work unless a test adds it. Start the API factory
///     first: it applies the migrations.
/// </summary>
internal static class BotTestHost
{
    internal static IHost Create(Action<IServiceCollection>? configure = null, TimeProvider? clock = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = TestDatabase.ConnectionString,
                ["Outbox:PollingInterval"] = TimeSpan.FromMilliseconds(50).ToString(),

                // The template upgrade is tested by calling its command; a test host's service would change strategies of other tests.
                ["Strategies:TemplateUpgrades:Enabled"] = "false"
            });

        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.Services.AddSingleton(clock ?? TimeProvider.System);
        builder.Services.AddProfitGraphInfrastructure(builder.Configuration);
        configure?.Invoke(builder.Services);

        return builder.Build();
    }
}