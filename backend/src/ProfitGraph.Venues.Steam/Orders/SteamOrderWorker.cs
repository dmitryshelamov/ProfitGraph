using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Venues.Steam.Confirmations;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Moves every Steam account with open orders along, one account at a time, every
///     <see cref="SteamOptions.OrderWorkerInterval" />: buys waiting for their slot or deadline, sells waiting for their
///     unlock or listing, mobile confirmations, and fills from market history. A round that cannot decide something leaves
///     it for the next; a failure is logged by kind only, never with Steam's text or the account's secrets.
/// </summary>
internal sealed class SteamOrderWorker(
    ISteamOrderJournal journal,
    ISteamBotAccounts accounts,
    SteamBuyOrders buys,
    SteamSells sells,
    SteamConfirmations confirmations,
    SteamExecutions executions,
    IOptions<SteamOptions> options,
    TimeProvider clock,
    ILogger<SteamOrderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Round(stoppingToken);

            try
            {
                await Task.Delay(options.Value.OrderWorkerInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    ///     One pass over the accounts with open orders.
    /// </summary>
    public async Task Round(CancellationToken cancellationToken)
    {
        IReadOnlyList<(Guid CredentialId, Guid BotId)> open;

        try
        {
            open = await journal.OpenAccounts(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("The Steam order journal could not be read: {Failure}", exception.GetType().Name);

            return;
        }

        foreach ((Guid credentialId, Guid botId) in open)
        {
            try
            {
                SteamBotAccount bot = await accounts.Get(botId, cancellationToken);
                await buys.Advance(bot.OwnerId, credentialId, cancellationToken);
                await sells.Advance(bot.OwnerId, credentialId, cancellationToken);
                await confirmations.Advance(bot.OwnerId, credentialId, cancellationToken);

                if (bot.SellerFees is { } fees)
                {
                    await executions.Advance(bot.OwnerId, credentialId, bot.Instrument.Currency, fees, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("A Steam account's orders could not be moved along: {Failure}", exception.GetType().Name);
            }
        }
    }
}