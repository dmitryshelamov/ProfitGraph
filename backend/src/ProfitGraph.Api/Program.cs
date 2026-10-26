using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using ProfitGraph.Api.Backtesting.Realtime;
using ProfitGraph.Api.Bots.Realtime;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Common.Errors;
using ProfitGraph.Api.MarketData;
using ProfitGraph.Api.Optimization.Realtime;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Infrastructure;
using ProfitGraph.Infrastructure.Bots.FormingCandles;
using ProfitGraph.Infrastructure.Common;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Venues.Bybit;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.TInvest;

using Serilog;
using Serilog.Formatting.Json;

namespace ProfitGraph.Api;

public class Program
{
    public static void Main(string[] args)
    {
        bool migrateOnly = args.Contains("--migrate-only", StringComparer.Ordinal);
        WebApplicationBuilder builder = WebApplication.CreateBuilder([.. args.Where(argument => argument != "--migrate-only")]);

        builder.Services.AddSerilog(
            (_, configuration) => configuration
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console(new JsonFormatter()),
            preserveStaticLogger: true);
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            HttpErrors.Enrich(context.ProblemDetails, context.HttpContext));
        builder.Services.AddExceptionHandler<HttpExceptionHandler>();
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = HttpErrors.InvalidModel);

        // The OpenAPI document reads HTTP JSON options; without Strict every integer is described as "number | string".
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        builder.Services.AddOpenApi();
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProfitGraphInfrastructure(builder.Configuration);
        builder.Services.AddBybitVenue(builder.Configuration);
        builder.Services.AddSteamVenue(builder.Configuration);
        builder.Services.AddTInvestVenue(builder.Configuration);
        builder.Services.AddScoped<CsvImportLimitsFilter>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<BacktestProgressSubscriptions>();
        builder.Services.AddHostedService<BacktestProgressPublisher>();
        builder.Services.AddSingleton<OptimizationProgressSubscriptions>();
        builder.Services.AddHostedService<OptimizationProgressPublisher>();
        builder.Services.AddHostedService<LivePricePublisher>();
        builder.Services.AddSingleton<IFormingCandleSink, FormingCandleSink>();
        builder.Services.AddFormingCandleFeeds();
        builder.AddProfitGraphAuthentication();

        WebApplication app = builder.Build();

        if (migrateOnly)
        {
            app.Services.MigratePostgres();

            return;
        }

        if (app.Services.ShouldMigrateOnStartup())
        {
            app.Services.MigratePostgres();
        }

        app.BootstrapOwner();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
        app.UseStatusCodePages();

        app.UseProfitGraphAuthentication();

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            app.MapOpenApi().AllowAnonymous();
        }

        app.MapControllers();
        app.MapHub<BacktestHub>(BacktestHub.Path);
        app.MapHub<OptimizationHub>(OptimizationHub.Path);
        app.MapHub<LivePriceHub>(LivePriceHub.Path);
        app.MapHub<FormingCandleHub>(FormingCandleHub.Path);
        app.MapHealthChecks("/api/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/api/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) }).AllowAnonymous();

        app.Run();
    }
}