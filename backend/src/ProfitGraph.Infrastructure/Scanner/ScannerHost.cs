using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Infrastructure.Scanner;

/// <summary>
///     The Worker's <c>scanner</c> role (ADR 0059): while it holds <see cref="ScannerLock" />, it scans every venue that has an
///     <see cref="IMarketScanSource" />; when the lock's connection fails, it stops and waits to take the lock again.
/// </summary>
internal sealed class ScannerHost(
    ScannerLock scannerLock,
    IServiceScopeFactory scopes,
    IOptions<ScannerOptions> options,
    TimeProvider clock,
    ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger _logger = loggers.CreateLogger<ScannerHost>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using NpgsqlConnection? held = await scannerLock.TryAcquire(stoppingToken);

                if (held is not null)
                {
                    _logger.LogInformation("The scanner lock is taken; scanning starts.");
                    await Scan(held, stoppingToken);
                    _logger.LogWarning("The scanner lock was lost; scanning stopped.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning("The scanner failed with {ExceptionType}; it starts again.", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(options.Value.LockCheckInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task Scan(NpgsqlConnection held, CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        using var scanning = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        Task[] venues =
        [
            .. scope.ServiceProvider.GetServices<IMarketScanSource>()
                .Select(source => new VenueScanRunner(source, scopes, options.Value, clock, loggers.CreateLogger<VenueScanRunner>()).Run(scanning.Token))
        ];

        await ScannerLock.Hold(held, options.Value.LockCheckInterval, clock, stoppingToken);
        await scanning.CancelAsync();
        await Task.WhenAll(venues);
    }
}