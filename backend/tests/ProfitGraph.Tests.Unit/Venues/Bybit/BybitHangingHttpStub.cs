namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     Hangs on the first request until it is cancelled, then answers.
/// </summary>
internal sealed class BybitHangingHttpStub(Func<HttpResponseMessage> response) : HttpMessageHandler
{
    public int Requests { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (++Requests == 1)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return response();
    }
}