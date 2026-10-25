using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     What a Bybit call for a bot needs: its symbol and rules, and its credential decrypted in memory.
/// </summary>
internal sealed record BybitBotContext(BotId BotId, string Symbol, InstrumentRulesDefinitionInternal Instrument, ExchangeCredentialSecretsInternal Credential);
