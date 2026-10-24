using ProfitGraph.Infrastructure;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Bots.Runtime;
using ProfitGraph.Infrastructure.Common;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Venues.Bybit;

using Serilog;
using Serilog.Formatting.Json;

namespace ProfitGraph.Worker;

/// <summary>
///     Runs background jobs (ADR 0011) and the live bot runtime, by its configured roles (<see cref="WorkerRoles" />);
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
        builder.Services.AddProfitGraphInfrastructure(builder.Configuration);
        IReadOnlySet<string> roles = WorkerRoles.Read(builder.Configuration);

        if (roles.Contains(WorkerRoles.Jobs))
        {
            builder.Services.AddBackgroundJobRunner();
            builder.Services.AddBacktestCompaction();
        }

        if (roles.Contains(WorkerRoles.Live))
        {
            builder.Services.AddOutboxDispatcher();
            builder.Services.AddBotRuntime();
        }

        builder.Services.AddBybitVenue(builder.Configuration);

        IHost host = builder.Build();
        host.Run();
    }
}