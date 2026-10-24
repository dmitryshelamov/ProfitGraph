using System.Text.Json.Serialization;

using ProfitGraph.Api.Common.Errors;
using ProfitGraph.Infrastructure;

using Serilog;
using Serilog.Formatting.Json;

namespace ProfitGraph.Api;

public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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
        builder.Services.AddProfitGraphInfrastructure();

        WebApplication app = builder.Build();

        app.UseSerilogRequestLogging();
        app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapControllers();
        app.MapHealthChecks("/api/health");

        app.Run();
    }
}