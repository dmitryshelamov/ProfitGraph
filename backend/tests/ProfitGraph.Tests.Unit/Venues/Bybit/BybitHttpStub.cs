namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     Answers Bybit requests in the test process and records them; no request leaves the machine.
/// </summary>
internal sealed class BybitHttpStub(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        return Task.FromResult(respond(request.RequestUri!));
    }
}