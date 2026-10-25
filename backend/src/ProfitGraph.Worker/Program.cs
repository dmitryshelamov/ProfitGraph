using ProfitGraph.Infrastructure;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Bots.Runtime;
using ProfitGraph.Infrastructure.Common;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Infrastructure.Notifications;
using ProfitGraph.Venues.Bybit;
using ProfitGraph.Venues.Steam;

using Serilog;
using Serilog.Formatting.Json;

namespace ProfitGraph.Worker;

/// <summary>
///     Runs background jobs (ADR 0011) and the live bot runtime, by its configured roles (<see cref="WorkerRoles" />);
///     trading stays disabled unless explicitly enabled by configuration. End-to-end tests may trade against an in-memory
///     simulation instead, only in the Testing environment.
/// </summary>
public static class Program
{
    private const string TestingEnvironment = "Testing";

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
            builder.Services.AddTelegramNotifications(builder.Configuration);

            var tradingEnabled = builder.Configuration.GetValue<bool>("Live:Trading:Enabled");
            var simulated = builder.Configuration.GetValue<bool>("Live:Simulation:Enabled");

            if (simulated && (tradingEnabled || !builder.Environment.IsEnvironment(TestingEnvironment)))
            {
                throw new InvalidOperationException("Live:Simulation:Enabled is allowed only in the Testing environment with trading disabled.");
            }

            if (tradingEnabled)
            {
                builder.Services.AddBybitTrading();
                builder.Services.AddSteamTrading();
            }
            else if (simulated)
            {
                builder.Services.AddBybitSimulatedTrading(builder.Configuration);
            }
        }

        builder.Services.AddBybitVenue(builder.Configuration);
        builder.Services.AddSteamVenue(builder.Configuration);

        IHost host = builder.Build();

        if (OperatorCommands.Requested(args) is { } command)
        {
            Environment.ExitCode = OperatorCommands.Run(host.Services, command, Console.In, Console.Out).GetAwaiter().GetResult();

            return;
        }

        host.Run();
    }
}