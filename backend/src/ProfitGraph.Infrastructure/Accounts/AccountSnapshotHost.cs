using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Application.Accounts.RecordAccountBalance;
using ProfitGraph.Application.Accounts.RecordAccountIdentity;
using ProfitGraph.Application.Accounts.ThinAccountSnapshots;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts.BalanceStatuses;
using ProfitGraph.Domain.Accounts.Identities;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Infrastructure.Accounts;

/// <summary>
///     The Worker's <c>live</c> role reads the account value of every credential with a bot, stopped or running and even
///     while trading is halted (Profit Fox Designer ADR 0069): a credential is due five minutes after a success, and after
///     a failure 5, 10, 20, 40 and then 60 minutes later. Failures stay per credential. No transaction spans a venue read.
///     History older than <see cref="AccountSnapshotOptions.FullDetail" /> is thinned to hourly every
///     <see cref="AccountSnapshotOptions.ThinInterval" />. The account's identity, on a venue that describes
///     it, is read once a day and an hour after a failure (ADR 0049), never on a page view.
/// </summary>
internal sealed class AccountSnapshotHost(
    IServiceScopeFactory scopes,
    IOptions<AccountSnapshotOptions> options,
    TimeProvider clock,
    ILogger<AccountSnapshotHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, clock);
        var thinnedAt = DateTimeOffset.MinValue;

        try
        {
            do
            {
                try
                {
                    await ReadDue(stoppingToken);
                    await ReadDueIdentities(stoppingToken);

                    if (clock.GetUtcNow() - thinnedAt >= options.Value.ThinInterval)
                    {
                        using IServiceScope scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ThinAccountSnapshotsCommand(), stoppingToken);
                        thinnedAt = clock.GetUtcNow();
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Reading account values failed with {ExceptionType}.", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown is not a failed read.
        }
    }

    private async Task ReadDue(CancellationToken stoppingToken)
    {
        IReadOnlyList<DueAccountBalance> due;

        using (IServiceScope scope = scopes.CreateScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IAccountBalanceStatusRepository>().ListDue(clock.GetUtcNow(), stoppingToken);
        }

        foreach (DueAccountBalance credential in due)
        {
            using IServiceScope scope = scopes.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            AccountBalanceReadInternal read = await Read(scope.ServiceProvider, sender, credential, stoppingToken);
            await sender.Send(new RecordAccountBalanceCommand(credential.CredentialId.Value, read), stoppingToken);

            if (read.Observation is null)
            {
                logger.LogWarning("Reading the account value of credential {CredentialId} failed: {Reason}.", credential.CredentialId.Value, read.FailureReason);
            }
            else
            {
                logger.LogInformation("Stored the account value of credential {CredentialId}.", credential.CredentialId.Value);
            }
        }
    }

    private async Task ReadDueIdentities(CancellationToken stoppingToken)
    {
        IReadOnlyList<DueAccountIdentity> due;

        using (IServiceScope scope = scopes.CreateScope())
        {
            IReadOnlyList<VenueId> venues = scope.ServiceProvider.GetRequiredService<VenuePorts<IVenueAccountIdentityReader>>().Venues;
            due = await scope.ServiceProvider.GetRequiredService<IAccountIdentityRepository>().ListDue(venues, clock.GetUtcNow(), stoppingToken);
        }

        foreach (DueAccountIdentity credential in due)
        {
            using IServiceScope scope = scopes.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            AccountIdentityReadInternal read = await ReadIdentity(scope.ServiceProvider, sender, credential, stoppingToken);
            await sender.Send(new RecordAccountIdentityCommand(credential.CredentialId.Value, read), stoppingToken);

            if (read.Identity is null)
            {
                logger.LogWarning("Reading the account identity of credential {CredentialId} failed: {Reason}.", credential.CredentialId.Value, read.FailureReason);
            }
            else
            {
                logger.LogInformation("Stored the account identity of credential {CredentialId}.", credential.CredentialId.Value);
            }
        }
    }

    private static async Task<AccountIdentityReadInternal> ReadIdentity(
        IServiceProvider services,
        ISender sender,
        DueAccountIdentity due,
        CancellationToken stoppingToken)
    {
        try
        {
            ExchangeCredentialSecretsInternal secrets = await sender.Send(
                new ReadExchangeCredentialSecretsQuery(due.OwnerId.Value, due.CredentialId.Value),
                stoppingToken);
            IVenueAccountIdentityReader? reader = services.GetRequiredService<VenuePorts<IVenueAccountIdentityReader>>().Find(new VenueId(secrets.Venue));

            return reader is null
                ? AccountIdentityReadInternal.Failed("venue_unsupported")
                : await reader.Read(secrets, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AccountIdentityReadInternal.Failed("credential_unavailable", exception.GetType().Name);
        }
    }

    private static async Task<AccountBalanceReadInternal> Read(IServiceProvider services, ISender sender, DueAccountBalance due, CancellationToken stoppingToken)
    {
        try
        {
            ExchangeCredentialSecretsInternal secrets = await sender.Send(
                new ReadExchangeCredentialSecretsQuery(due.OwnerId.Value, due.CredentialId.Value),
                stoppingToken);
            IAccountBalanceReader? reader = services.GetRequiredService<VenuePorts<IAccountBalanceReader>>().Find(new VenueId(secrets.Venue));

            return reader is null
                ? AccountBalanceReadInternal.Failed("venue_unsupported")
                : await reader.Read(secrets, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AccountBalanceReadInternal.Failed("credential_unavailable", exception.GetType().Name);
        }
    }
}