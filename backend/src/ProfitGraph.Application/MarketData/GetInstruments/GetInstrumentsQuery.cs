using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.GetInstruments;

/// <summary>
///     Searches a venue's instruments; a named credential of the user reads them for a venue without anonymous market data
///     (ADR 0023, TC6).
/// </summary>
public sealed record GetInstrumentsQuery(string Venue, string Query, int Limit, Guid UserId = default, Guid? CredentialId = null)
    : IQuery<IReadOnlyList<InstrumentInternal>>;