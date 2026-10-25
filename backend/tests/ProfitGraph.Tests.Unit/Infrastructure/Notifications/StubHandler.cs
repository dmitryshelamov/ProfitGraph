using System.Net;

namespace ProfitGraph.Tests.Unit.Infrastructure.Notifications;

/// <summary>
///     Answers every request with one status and body and remembers the last path.
/// </summary>
internal sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public string? Path { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Path = request.RequestUri!.AbsolutePath;

        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}