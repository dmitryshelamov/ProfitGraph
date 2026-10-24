using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using ProfitGraph.Api.Common.Errors;
using ProfitGraph.Infrastructure;
using ProfitGraph.Infrastructure.Common;
using ProfitGraph.Infrastructure.Common.Postgres;

using Serilog;
using Serilog.Formatting.Json;

namespace ProfitGraph.Api;

public class Program
{
    public static void Main(string[] args)
    {
        bool migrateOnly = args.Contains("--migrate-only", StringComparer.Ordinal);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--migrate-only").ToArray());

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
            .AddJsonOptions(options => options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict)
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = HttpErrors.InvalidModel);
        builder.Services.AddOpenApi();
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProfitGraphInfrastructure(builder.Configuration);

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

        app.UseSerilogRequestLogging();
        app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapControllers();
        app.MapHealthChecks("/api/health", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/api/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) });

        app.Run();
    }
}