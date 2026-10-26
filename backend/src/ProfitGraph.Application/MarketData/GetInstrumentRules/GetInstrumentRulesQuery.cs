using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.GetInstrumentRules;

/// <summary>
///     Reads a venue's current rules of one instrument without capturing them, so a form can follow its price tick; a named
///     credential of the user reads them for a venue without anonymous market data (ADR 0023, TC6).
/// </summary>
public sealed record GetInstrumentRulesQuery(string Venue, string Symbol, Guid UserId = default, Guid? CredentialId = null)
    : IQuery<InstrumentRulesDefinitionInternal>;