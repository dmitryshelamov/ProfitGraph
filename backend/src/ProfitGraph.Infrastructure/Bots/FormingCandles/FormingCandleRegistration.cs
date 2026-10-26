using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Bots.Ports;

namespace ProfitGraph.Infrastructure.Bots.FormingCandles;

public static class FormingCandleRegistration
{
    /// <summary>
    ///     Registers the forming-candle feeds for a process that serves charts (the API, ADR 0026). The caller registers the
    ///     <see cref="IFormingCandleSink" /> that delivers to its clients.
    /// </summary>
    public static IServiceCollection AddFormingCandleFeeds(this IServiceCollection services)
    {
        services.AddSingleton(new FormingCandleOptions());
        services.AddSingleton<IFormingCandleFeeds, FormingCandleFeeds>();

        return services;
    }
}