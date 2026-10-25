using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     What a sell's journal entry shows the engine, from its reserved assets (ADR 0022).
/// </summary>
internal sealed class SteamSellSettlement(ISteamOrderJournal journal, SteamOrderUpdates updates, TimeProvider clock)
{
    /// <summary>
    ///     Brings the entry in line with its assets: active and accepted once a listing is live, waiting for the hold while
    ///     only locked assets remain, rejected when Steam refused every one. The engine hears of a change, and of the current
    ///     state when <paramref name="announce" /> asks for it, as a submit does.
    /// </summary>
    public async Task Settle(SteamOrderJournalEntry entry, CancellationToken cancellationToken, bool announce = false)
    {
        IReadOnlyList<SteamAssetReservation> reservations = await journal.Reservations(entry.ClientOrderId, cancellationToken);
        SteamOrderJournalEntry current = await journal.Get(entry.ClientOrderId, cancellationToken) ?? throw Unresolved();

        if (current.State is SteamOrderJournalState.Cancelling or SteamOrderJournalState.Done)
        {
            return;
        }

        if (reservations.All(reservation => reservation.State == SteamReservationState.Released))
        {
            await journal.Transition(Done(current, clock.GetUtcNow()), current.State, cancellationToken);
            updates.Publish(current.BotId, ExternalOrderUpdate.Reject(current.EngineOrderId, RejectionReason.BrokerRejected, "Steam refused every listing."));

            return;
        }

        if (reservations.Any(reservation => reservation.State == SteamReservationState.Listed))
        {
            bool activated = current.State == SteamOrderJournalState.Waiting && await journal.Transition(
                current with { State = SteamOrderJournalState.Active, WaitingCode = null, UpdatedAt = clock.GetUtcNow() },
                SteamOrderJournalState.Waiting,
                cancellationToken);

            if (activated || announce)
            {
                updates.Publish(current.BotId, ExternalOrderUpdate.Accept(current.EngineOrderId));
            }

            return;
        }

        bool held = reservations.All(reservation => reservation.State is SteamReservationState.Reserved or SteamReservationState.Released);
        SteamWaitingCode? code = held ? SteamWaitingCode.SettlementHold : SteamWaitingCode.AwaitingConfirmation;

        if (current.State == SteamOrderJournalState.Waiting && current.WaitingCode != code)
        {
            await journal.Transition(current with { WaitingCode = code, UpdatedAt = clock.GetUtcNow() }, SteamOrderJournalState.Waiting, cancellationToken);
        }

        if (announce || current.WaitingCode is null)
        {
            updates.Publish(current.BotId, ExternalOrderUpdate.AcceptLocally(current.EngineOrderId));
        }
    }

    private static SteamOrderJournalEntry Done(SteamOrderJournalEntry entry, DateTimeOffset at)
    {
        return entry with { State = SteamOrderJournalState.Done, Outcome = SteamOrderOutcome.Rejected, WaitingCode = null, UpdatedAt = at };
    }

    private static SteamOrderUnresolvedException Unresolved()
    {
        return new SteamOrderUnresolvedException("The Steam sell's state is not proved.");
    }
}