using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     Sends one request to a configured Steam host and classifies the answer (Profit Fox Designer ADRs 0081 and 0083). It
///     follows no redirect, keeps no cookies of its own, reads at most the configured body size and retries nothing; the
///     caller attaches cookies and decides what to do next. It logs only that a read got an unexpected answer, with its status
///     code, content type and the path of a redirect, so no cookie, account number or body reaches a log.
/// </summary>
internal sealed partial class SteamHttpTransport(HttpClient client, IOptions<SteamOptions> options, ILogger<SteamHttpTransport> logger)
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

        if (request.Endpoint == SteamEndpoint.Community)
        {
            Ajax(message, origin, request);
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
                response.Headers.Location,
                response.Headers.TryGetValues("x-eresult", out IEnumerable<string>? results) &&
                int.TryParse(results.FirstOrDefault(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int result)
                    ? result
                    : null);

            SteamRequestOutcome outcome = Classify(response, body, request, destination);

            if (outcome == SteamRequestOutcome.InvalidResponse)
            {
                logger.LogWarning(
                    "A Steam read of {Path} got an unexpected answer: status {Status}, {ContentType}, redirect to {Redirect}.",
                    Masked(request.Path),
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.MediaType,
                    response.Headers.Location is { } location ? Masked(location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0]) : null);
            }

            return new SteamRequestResult(outcome, payload, RetryAfter(response.Headers.RetryAfter));
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException ||
                                          (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SteamRequestResult(request.IsRead ? SteamRequestOutcome.TransientFailure : SteamRequestOutcome.Unknown);
        }
        catch (InvalidDataException)
        {
            logger.LogWarning("A Steam read of {Path} had a body that could not be decoded.", Masked(request.Path));

            return new SteamRequestResult(request.IsRead ? SteamRequestOutcome.InvalidResponse : SteamRequestOutcome.Unknown);
        }
    }

    /// <summary>
    ///     Market requests other than page navigations, and mobile confirmations, carry the AJAX headers Steam's own pages send,
    ///     with the item's listing page as the referrer when the request names an item (Profit Fox Designer
    ///     <c>SteamMarketRequestHeaders</c>, which traded with them). The eligibility check asks for a page, as a browser does.
    /// </summary>
    private static void Ajax(HttpRequestMessage message, Uri community, SteamHttpRequest request)
    {
        string path = message.RequestUri!.AbsolutePath;

        if (request.ExpectsRedirect)
        {
            message.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");

            return;
        }

        if (request.FollowsRedirects)
        {
            return;
        }

        if (path.StartsWith("/mobileconf/", StringComparison.Ordinal))
        {
            message.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            message.Headers.Referrer = new Uri(community, "/mobileconf/conf");

            return;
        }

        if (!path.StartsWith("/market/", StringComparison.Ordinal) || path == "/market/")
        {
            return;
        }

        message.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string pair in message.RequestUri.Query.TrimStart('?').Split(separator: '&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split(separator: '=', count: 2);
            values.TryAdd(WebUtility.UrlDecode(parts[0]), parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty);
        }

        foreach (string key in new[] { "appid", "market_hash_name" })
        {
            if (request.Form?.TryGetValue(key, out string? value) == true)
            {
                values[key] = value;
            }
        }

        message.Headers.Referrer = values.TryGetValue("appid", out string? app) &&
            int.TryParse(app, NumberStyles.None, CultureInfo.InvariantCulture, out int appId) && appId > 0 &&
            values.TryGetValue("market_hash_name", out string? name) && name.Length is > 0 and <= 512 && !name.Any(char.IsControl)
                ? new Uri(community, "/market/listings/" + appId.ToString(CultureInfo.InvariantCulture) + "/" + Uri.EscapeDataString(name))
                : new Uri(community, "/market/");
    }

    /// <summary>
    ///     A request path without its query and with account and asset numbers hidden.
    /// </summary>
    private static string Masked(string path)
    {
        return Digits().Replace(path.Split('?')[0], "#");
    }

    [GeneratedRegex(@"\d{6,}")]
    private static partial Regex Digits();

    private static SteamRequestOutcome Classify(HttpResponseMessage response, byte[] body, SteamHttpRequest request, Uri destination)
    {
        bool isRead = request.IsRead;
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

        if (status is >= 300 and < 400 && request.FollowsRedirects)
        {
            return WithinHost(response.Headers.Location, destination) ? SteamRequestOutcome.Success : SteamRequestOutcome.InvalidResponse;
        }

        if (status is >= 300 and < 400)
        {
            return request.ExpectsRedirect ? SteamRequestOutcome.Success : isRead ? SteamRequestOutcome.InvalidResponse : SteamRequestOutcome.Unknown;
        }

        if (status >= 400)
        {
            return SteamRequestOutcome.ClientError;
        }

        return Flagged(body) ?? SteamRequestOutcome.Success;
    }

    /// <summary>
    ///     A followed redirect stays on the host it was sent to, without user info, a fragment or a backslash.
    /// </summary>
    private static bool WithinHost(Uri? location, Uri destination)
    {
        return location is not null && !location.OriginalString.Contains('\\') && Uri.TryCreate(destination, location, out Uri? target) &&
            target.Scheme == destination.Scheme && target.Authority == destination.Authority && target.UserInfo.Length == 0 &&
            target.Fragment.Length == 0 && !target.AbsolutePath.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsLogin(Uri? location)
    {
        return location is not null && (location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString)
            .StartsWith("/login", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The flags of a JSON answer that end its success: <c>needauth</c>, <c>captcha_needed</c>, <c>market_restricted</c>.
    /// </summary>
    private static SteamRequestOutcome? Flagged(byte[] body)
    {
        if (body.Length == 0 || body[0] != (byte)'{')
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            return Flag(root, "needauth") ? SteamRequestOutcome.Unauthorized
                : Flag(root, "captcha_needed") ? SteamRequestOutcome.Captcha
                : Flag(root, "market_restricted") ? SteamRequestOutcome.Restricted
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Flag(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
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