using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Accounts.CreateExchangeCredential;

/// <summary>
///     A credential that selects a venue account (ADR 0023, TC5) is saved only if the venue lists that account as eligible
///     right now, and only once per venue account: its keyed fingerprint is unique per venue. Its public hint is the end of
///     the account identifier.
/// </summary>
internal sealed class CreateExchangeCredentialCommandHandler(
    IExchangeCredentialRepository credentials,
    ISecretProtector protector,
    VenuePorts<IVenueAccountDirectory> directories,
    ITransactionExecutor transactions,
    TimeProvider clock)
    : IRequestHandler<CreateExchangeCredentialCommand, ExchangeCredentialInternal>
{
    private const string AccountPurpose = "venue-account";

    public async Task<ExchangeCredentialInternal> Handle(CreateExchangeCredentialCommand command, CancellationToken cancellationToken)
    {
        protector.EnsureAvailable();
        var id = ExchangeCredentialId.New();
        var ownerId = new UserId(command.OwnerId);
        var venue = new VenueId(command.Venue!);
        ExchangeCredential credential = command.Fields is { } fields && directories.Find(venue) is { } directory
            ? await WithAccount(id, ownerId, venue, command.Label, fields, directory, cancellationToken)
            : WithKeyAndSecret(id, ownerId, venue, command);
        bool added = await transactions.ExecuteAsync(() => credentials.Add(credential, cancellationToken), cancellationToken);

        return added
            ? credential.ToInternal()
            : throw new StateConflictException("This venue account already has a credential; delete it first.", ExchangeCredentialErrorCodes.AccountExists);
    }

    private ExchangeCredential WithKeyAndSecret(ExchangeCredentialId id, UserId ownerId, VenueId venue, CreateExchangeCredentialCommand command)
    {
        string apiKey = command.ApiKey!.Trim();

        return ExchangeCredential.Create(
            id,
            ownerId,
            venue,
            new ExchangeCredentialLabel(command.Label),
            ApiKeyLast4.Of(apiKey),
            new Dictionary<string, EncryptedSecret>(StringComparer.Ordinal)
            {
                [ExchangeCredentialFields.ApiKey] = protector.Encrypt(id, ownerId, ExchangeCredentialFields.ApiKey, apiKey),
                [ExchangeCredentialFields.ApiSecret] = protector.Encrypt(id, ownerId, ExchangeCredentialFields.ApiSecret, command.ApiSecret!.Trim())
            },
            clock.GetUtcNow());
    }

    private async Task<ExchangeCredential> WithAccount(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        string? label,
        IReadOnlyDictionary<string, string> fields,
        IVenueAccountDirectory directory,
        CancellationToken cancellationToken)
    {
        string accountId = fields[directory.AccountField];
        IReadOnlyList<VenueAccountInternal> accounts = await directory.List(
            fields.Where(field => field.Key != directory.AccountField).ToDictionary(StringComparer.Ordinal),
            cancellationToken);

        if (accounts.SingleOrDefault(account => account.Id == accountId) is not { Eligible: true })
        {
            throw new ApplicationValidationException(
            [
                new InputError(
                    ExchangeCredentialErrorCodes.AccountIneligible,
                    $"Fields.{directory.AccountField}",
                    "The venue does not list this account as one a bot may trade through.")
            ]);
        }

        IReadOnlyList<string> fingerprints = protector.Fingerprints(AccountPurpose, $"{venue.Value}:{accountId}");

        if (await credentials.AnyAccountFingerprint(venue, fingerprints, cancellationToken))
        {
            throw new StateConflictException("This venue account already has a credential; delete it first.", ExchangeCredentialErrorCodes.AccountExists);
        }

        return ExchangeCredential.Create(
            id,
            ownerId,
            venue,
            new ExchangeCredentialLabel(label),
            new ApiKeyLast4(accountId.Length >= ApiKeyLast4.Length ? accountId[^ApiKeyLast4.Length..] : accountId.PadLeft(ApiKeyLast4.Length, paddingChar: '0')),
            fields.ToDictionary(field => field.Key, field => protector.Encrypt(id, ownerId, field.Key, field.Value), StringComparer.Ordinal),
            clock.GetUtcNow(),
            fingerprints[0]);
    }
}