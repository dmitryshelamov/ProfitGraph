using MediatR;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;

namespace ProfitGraph.Application.Scanner.SetScanTradeSettings;

internal sealed class SetScanTradeSettingsCommandHandler(
    IEnumerable<IMarketScanSource> sources,
    IExchangeCredentialRepository credentials,
    IScanTradeSettingsRepository settings,
    VenueRegistry venues,
    VenuePorts<IVenueLeverage> leverages,
    TimeProvider clock) : IRequestHandler<SetScanTradeSettingsCommand, ScanTradeSettingsInternal>
{
    public async Task<ScanTradeSettingsInternal> Handle(SetScanTradeSettingsCommand command, CancellationToken cancellationToken)
    {
        sources.Find(command.Venue);
        var owner = new UserId(command.OwnerId);
        var venue = new VenueId(command.Venue);
        ExchangeCredential credential = await credentials.GetOwned(command.CredentialId, command.OwnerId, cancellationToken);

        if (!venues.AcceptsCredential(venue, credential.Venue))
        {
            throw new ApplicationValidationException(
                [new InputError(ScanTradeErrorCodes.WrongVenue, nameof(command.CredentialId), "The credential belongs to another venue.")]);
        }

        bool margin = leverages.Find(venue) is not null;

        if (!margin && command.Leverage is not null)
        {
            throw new ApplicationValidationException([new InputError("invalid_value", nameof(command.Leverage), "This venue has no leverage.")]);
        }

        decimal? leverage = margin ? command.Leverage ?? 1m : null;
        DateTimeOffset now = clock.GetUtcNow();
        ScanTradeSettings? stored = await settings.Get(owner, venue, cancellationToken);
        ScanTradeSettings saved = stored ?? ScanTradeSettings.Create(owner, venue, credential.Id, command.Risk, command.MaxAmount, now, leverage);

        if (stored is not null)
        {
            saved.Change(credential.Id, command.Risk, command.MaxAmount, now, leverage);
        }

        await settings.Save(saved, cancellationToken);

        return saved.ToInternal();
    }
}