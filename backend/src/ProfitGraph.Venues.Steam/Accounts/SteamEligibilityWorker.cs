using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     Checks accounts in <see cref="SteamGuardStatus.Checking" />, or a new authenticator's account in
///     <see cref="SteamGuardStatus.CoolingDown" /> once its expected aging passed, whose check is due, every
///     <see cref="SteamOptions.EligibilityInterval" />. The attempt is recorded before Steam is asked, so a failed or
///     concurrent check waits <see cref="SteamOptions.EligibilityRecheck" />; an account still unconfirmed is checked again
///     after that, and a ready or restricted one is left to its owner. Failures are logged by kind only, and one account's
///     failure does not keep the others from being checked.
/// </summary>
internal sealed class SteamEligibilityWorker(
    IServiceScopeFactory scopes,
    SteamEligibility eligibility,
    IOptions<SteamOptions> options,
    TimeProvider clock,
    ILogger<SteamEligibilityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Round(stoppingToken);

            try
            {
                await Task.Delay(options.Value.EligibilityInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task Round(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ISteamAccountStore>();
            var credentials = scope.ServiceProvider.GetRequiredService<IExchangeCredentialRepository>();

            foreach (SteamAccount account in await store.Due(clock.GetUtcNow(), cancellationToken))
            {
                try
                {
                    await Check(account, store, credentials, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning("A Steam account could not be checked: {Failure}", exception.GetType().Name);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Steam accounts could not be checked: {Failure}", exception.GetType().Name);
        }
    }

    private async Task Check(SteamAccount account, ISteamAccountStore store, IExchangeCredentialRepository credentials, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        if (await credentials.Get(new ExchangeCredentialId(account.CredentialId), cancellationToken) is not { } credential ||
            !await store.Record(
                account.CredentialId,
                account.GuardStatus,
                SteamGuardStatus.Checking,
                now + options.Value.EligibilityRecheck,
                now,
                cancellationToken))
        {
            return;
        }

        SteamGuardStatus status = await eligibility.Check(credential.OwnerId.Value, account.CredentialId, cancellationToken);

        if (status != SteamGuardStatus.Checking)
        {
            await store.Record(account.CredentialId, SteamGuardStatus.Checking, status, nextCheckAt: null, clock.GetUtcNow(), cancellationToken);
        }
    }
}