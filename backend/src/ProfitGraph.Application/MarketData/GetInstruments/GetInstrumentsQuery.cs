using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.GetInstruments;

public sealed record GetInstrumentsQuery(string Venue, string Query, int Limit) : IQuery<IReadOnlyList<InstrumentInternal>>;