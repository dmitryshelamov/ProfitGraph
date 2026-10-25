using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     What a T-Invest call for a bot needs: its token and account from the decrypted credential, and its share.
/// </summary>
internal sealed record TInvestBotContext(BotId BotId, TInvestToken Token, string AccountId, TInvestShare Share);