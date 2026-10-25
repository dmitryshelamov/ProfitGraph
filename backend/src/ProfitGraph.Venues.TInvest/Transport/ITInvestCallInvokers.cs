using Grpc.Core;

namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     Where Invest API calls go: the configured channel, or an in-process mock in tests.
/// </summary>
internal interface ITInvestCallInvokers
{
    CallInvoker Get();
}