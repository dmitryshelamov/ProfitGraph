using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Options;

namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     Sends one request to a configured Steam host and classifies the answer (Profit Fox Designer ADRs 0081 and 0083). It
///     follows no redirect, keeps no cookies of its own, reads at most the configured body size and retries nothing; the
///     caller attaches cookies and decides what to do next. It logs nothing, so no cookie or body reaches a log.
/// </summary>
internal sealed class SteamHttpTransport(HttpClient client, IOptions<SteamOptions> options)
{
    private const string UserAgent = "ProfitGraph/SteamIntegration";

    public async Task<SteamRequestResult> Send(SteamHttpRequest request, string? cookies, CancellationToken cancellationToken)
    {
        SteamOptions settings = options.Value;

        if (!settings.Enabled || !settings.IsValid())
        {
            return new SteamRequestResult(SteamRequestOutcome.NotSent);
        }

        Uri origin = settings.Address(request.Endpoint);

        if (!request.Path.StartsWith('/') || request.Path.StartsWith("//", StringComparison.Ordinal) || request.Path.Contains('\\'))
        {
            throw new ArgumentException("A Steam request path is relative to its host.", nameof(request));
        }

        var destination = new Uri(origin, request.Path);

        if (destination.Scheme != origin.Scheme || destination.Authority != origin.Authority)
        {
            throw new ArgumentException("A Steam request cannot change its host.", nameof(request));
        }

        using var message = new HttpRequestMessage(request.Form is null ? HttpMethod.Get : HttpMethod.Post, destination);
        message.Headers.UserAgent.ParseAdd(UserAgent);

        if (cookies is not null)
        {
            message.Headers.TryAddWithoutValidation("Cookie", cookies);
        }

        if (request.Form is { } form)
        {
            message.Content = request.Multipart ? Multipart(form) : new FormUrlEncodedContent(form);
            message.Headers.TryAddWithoutValidation("Origin", settings.Address(SteamEndpoint.Community).GetLeftPart(UriPartial.Authority));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.RequestTimeout);

        try
        {
            using HttpResponseMessage response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            byte[] body = WithoutByteOrderMark(await ReadBody(response.Content, settings.MaximumResponseBytes, timeout.Token));
            var payload = new SteamHttpResponse(
                (int)response.StatusCode,
                body,
                response.Content.Headers.ContentType?.MediaType,
                response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies) ? [.. setCookies] : [],
                response.Headers.Location);

            return new SteamRequestResult(Classify(response, body, request.IsRead), payload, RetryAfter(response.Headers.RetryAfter));
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException ||
                                          (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SteamRequestResult(request.IsRead ? SteamRequestOutcome.TransientFailure : SteamRequestOutcome.Unknown);
        }
        catch (InvalidDataException)
        {
            return new SteamRequestResult(request.IsRead ? SteamRequestOutcome.InvalidResponse : SteamRequestOutcome.Unknown);
        }
    }

    private static SteamRequestOutcome Classify(HttpResponseMessage response, byte[] body, bool isRead)
    {
        var status = (int)response.StatusCode;

        if (response.StatusCode == HttpStatusCode.Unauthorized || (status is >= 300 and < 400 && IsLogin(response.Headers.Location)))
        {
            return SteamRequestOutcome.Unauthorized;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return SteamRequestOutcome.Forbidden;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return SteamRequestOutcome.RateLimited;
        }

        if (status >= 500)
        {
            return isRead ? SteamRequestOutcome.TransientFailure : SteamRequestOutcome.Unknown;
        }

        if (status is >= 300 and < 400)
        {
            return isRead ? SteamRequestOutcome.InvalidResponse : SteamRequestOutcome.Unknown;
        }

        if (status >= 400)
        {
            return SteamRequestOutcome.ClientError;
        }

        return NeedsAuthentication(body) ? SteamRequestOutcome.Unauthorized : SteamRequestOutcome.Success;
    }

    private static bool IsLogin(Uri? location)
    {
        return location is not null && (location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString)
            .StartsWith("/login", StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsAuthentication(byte[] body)
    {
        if (body.Length == 0 || body[0] != (byte)'{')
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty("needauth", out JsonElement value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? header)
    {
        return header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
    }

    private static MultipartFormDataContent Multipart(IReadOnlyDictionary<string, string> form)
    {
        var content = new MultipartFormDataContent();

        foreach ((string name, string value) in form)
        {
            content.Add(new StringContent(value), name);
        }

        return content;
    }

    private static async Task<byte[]> ReadBody(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit)
        {
            throw new InvalidDataException("The Steam response exceeds its configured limit.");
        }

        await using Stream input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];

        while (await input.ReadAsync(buffer, cancellationToken) is var count and > 0)
        {
            if (output.Length + count > limit)
            {
                throw new InvalidDataException("The Steam response exceeds its configured limit.");
            }

            output.Write(buffer, offset: 0, count);
        }

        return output.ToArray();
    }

    private static byte[] WithoutByteOrderMark(byte[] body)
    {
        return body is [0xEF, 0xBB, 0xBF, ..] ? body[3..] : body;
    }
}