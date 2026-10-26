using MediatR;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ImportExchangeCredential;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Venues.Steam.Persistence.Repositories;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     <c>--run steam-import</c>: stores an existing Steam Guard as a credential in <see cref="SteamGuardStatus.Checking" />
///     (Profit Fox Designer ADR 0083, owner amendment). It reads private JSON from stdin, contacts no Steam host and prints
///     only the credential's identifier and the last four digits of the Steam ID.
/// </summary>
internal sealed class SteamImportCommand(ISender sender, ISecretProtector protector, SteamAccountRepository accounts, TimeProvider clock) : IOperatorCommand
{
    public string Name => "steam-import";

    public async Task<int> Run(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        var buffer = new char[SteamImportInput.MaxBytes + 1];
        int length = await input.ReadBlockAsync(buffer, cancellationToken);

        if (length > SteamImportInput.MaxBytes)
        {
            await output.WriteLineAsync($"The input is longer than {SteamImportInput.MaxBytes} characters.");

            return 2;
        }

        (SteamImportInput? parsed, string? problem) = SteamImportInput.Parse(new string(buffer, startIndex: 0, length));

        if (parsed is null)
        {
            await output.WriteLineAsync(problem);

            return 2;
        }

        try
        {
            IReadOnlyList<string> fingerprints = protector.Fingerprints(SteamCredentialFields.AccountFingerprint, parsed.SteamId);

            if (await accounts.AnyFingerprint(fingerprints, cancellationToken))
            {
                await output.WriteLineAsync("This Steam account is already stored; delete its credential first.");

                return 1;
            }

            ExchangeCredentialInternal credential = await sender.Send(
                new ImportExchangeCredentialCommand(
                    parsed.Owner,
                    SteamVenue.VenueId.Value,
                    parsed.Label,
                    parsed.SteamId[^4..],
                    parsed.Fields(),
                    parsed.AccountName ?? parsed.SteamId),
                cancellationToken);

            if (!await accounts.Add(SteamAccount.Imported(credential.Id, fingerprints[0], clock.GetUtcNow()), cancellationToken))
            {
                await output.WriteLineAsync($"This Steam account was stored meanwhile; delete credential {credential.Id} on the keys page.");

                return 1;
            }

            await output.WriteLineAsync($"Imported Steam account ending {credential.ApiKeyLast4} as credential {credential.Id}; it is being checked.");

            return 0;
        }
        catch (ApplicationValidationException exception)
        {
            await output.WriteLineAsync($"Refused: {string.Join(", ", exception.Errors.Select(error => $"{error.Field} ({error.Code})"))}.");

            return 2;
        }
        catch (ApplicationExceptionBase exception)
        {
            await output.WriteLineAsync($"Refused: {exception.Code}.");

            return 2;
        }
    }
}