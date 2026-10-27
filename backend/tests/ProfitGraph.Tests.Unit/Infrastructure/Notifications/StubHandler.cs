using System.Net;

namespace ProfitGraph.Tests.Unit.Infrastructure.Notifications;

/// <summary>
///     Answers every request with one status and body and remembers the last path.
/// </summary>
internal sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public string? Path { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Path = request.RequestUri!.AbsolutePath;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}