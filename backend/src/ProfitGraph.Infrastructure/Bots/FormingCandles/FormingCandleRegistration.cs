using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Ports;

namespace ProfitGraph.Infrastructure.Bots.FormingCandles;

public static class FormingCandleRegistration
{
    /// <summary>
    ///     Registers the forming-candle feeds for a process that serves charts (the API, ADR 0026). The caller registers the
    ///     <see cref="IFormingCandleSink" /> that delivers to its clients.
    /// </summary>
    public static IServiceCollection AddFormingCandleFeeds(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FormingCandleOptions>()
            .Bind(configuration.GetSection(FormingCandleOptions.SectionName))
            .Validate(options => options.IsValid(), "Bots:FormingCandles needs positive delays and a maximum backoff no shorter than the first.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<FormingCandleOptions>>().Value);
        services.AddSingleton<IFormingCandleFeeds, FormingCandleFeeds>();

        return services;
    }
}