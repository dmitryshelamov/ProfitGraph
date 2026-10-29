using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanBacktest;

public sealed record GetScanBacktestQuery(Guid UserId, Guid BacktestId) : IQuery<ScanBacktestInternal>;