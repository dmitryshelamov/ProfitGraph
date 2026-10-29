using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanVenues;

public sealed record GetScanVenuesQuery : IQuery<IReadOnlyList<ScanVenueInternal>>;