using Grpc.Core;

using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

internal sealed class MockTInvestInvokers(MockTInvestApi api) : ITInvestCallInvokers
{
    public CallInvoker Get()
    {
        return api;
    }
}