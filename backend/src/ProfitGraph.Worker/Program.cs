using ProfitGraph.Infrastructure;
using ProfitGraph.Infrastructure.Jobs;

using Serilog;
using Serilog.Formatting.Json;

namespace ProfitGraph.Worker;

/// <summary>
///     Runs background jobs (ADR 0011) and, from stage 5, the live bot runtime; trading stays disabled unless explicitly
///     enabled by configuration.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSerilog(
            (_, configuration) => configuration
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console(new JsonFormatter()),
            preserveStaticLogger: true);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProfitGraphInfrastructure(builder.Configuration);
        builder.Services.AddBackgroundJobRunner();

        IHost host = builder.Build();
        host.Run();
    }
}