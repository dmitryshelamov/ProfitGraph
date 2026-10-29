using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Venues.TInvest.Scanner;

/// <summary>
///     The scanner's T-Invest credential (ADR 0062): <see cref="TInvestScannerOptions.CredentialId" />, decrypted in memory through
///     the application's credential path for its owner, its token reused for <see cref="TInvestScannerOptions.CredentialReuse" />. The Worker's scan
///     and backtests read with its token; the API opens it as market data access for the scanner's charts, levels and cards.
/// </summary>
internal sealed class TInvestScanMarketData(IServiceScopeFactory scopes, IOptions<TInvestOptions> options, TimeProvider clock) : IScanMarketDataAccess
{
    private readonly SemaphoreSlim _reading = new SemaphoreSlim(initialCount: 1, maxCount: 1);
    private (ExchangeCredentialSecretsInternal Credential, DateTimeOffset ReadAt)? _read;

    public VenueId Venue => TInvestVenue.VenueId;

    public async Task<ExchangeCredentialSecretsInternal> Credential(CancellationToken cancellationToken)
    {
        await _reading.WaitAsync(cancellationToken);

        try
        {
            if (_read is { } cached && clock.GetUtcNow() - cached.ReadAt < options.Value.Scanner.CredentialReuse)
            {
                return cached.Credential;
            }

            ExchangeCredentialSecretsInternal credential = await Read(cancellationToken);
            _read = (credential, clock.GetUtcNow());

            return credential;
        }
        finally
        {
            _reading.Release();
        }
    }

    public async Task<TInvestToken> Token(CancellationToken cancellationToken)
    {
        return new TInvestToken((await Credential(cancellationToken)).Field(TInvestCredentialFields.Token));
    }

    private async Task<ExchangeCredentialSecretsInternal> Read(CancellationToken cancellationToken)
    {
        Guid id = options.Value.Scanner.CredentialId ?? throw Unavailable("No T-Invest credential is configured for the scanner.");
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        try
        {
            ExchangeCredential? credential = await scope.ServiceProvider.GetRequiredService<IExchangeCredentialRepository>()
                .Get(new ExchangeCredentialId(id), cancellationToken);

            if (credential is null || credential.Venue != TInvestVenue.VenueId)
            {
                throw Unavailable("The scanner's credential is not a stored T-Invest credential.");
            }

            return await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new ReadExchangeCredentialSecretsQuery(credential.OwnerId.Value, id), cancellationToken);
        }
        catch (ApplicationExceptionBase exception) when (exception is not MarketScanException)
        {
            throw Unavailable($"The scanner's T-Invest credential cannot be read ({exception.Code}).");
        }
    }

    private static MarketScanException Unavailable(string message)
    {
        return new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, message);
    }
}