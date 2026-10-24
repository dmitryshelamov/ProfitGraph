using ProfitGraph.Infrastructure;

using Serilog;
using Serilog.Formatting.Json;

namespace ProfitGraph.Worker;

/// <summary>
///     Live runtime host. Bot runtime services are registered here as the live slice is ported;
///     trading stays disabled unless explicitly enabled by configuration.
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
        builder.Services.AddProfitGraphInfrastructure();

        IHost host = builder.Build();
        host.Run();
    }
}