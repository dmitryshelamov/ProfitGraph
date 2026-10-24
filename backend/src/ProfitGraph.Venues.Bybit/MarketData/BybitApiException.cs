using System.Globalization;
using System.Net;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     A failed Bybit call as the SDK reported it, with what decides a retry: the code, the HTTP status and the time
///     Bybit's rate limit resets.
/// </summary>
internal sealed class BybitApiException : Exception
{
    private const int RateLimited = 10006;

    public int? Code { get; }
    private HttpStatusCode? StatusCode { get; }
    public ErrorType ErrorType { get; }

    /// <summary>
    ///     When Bybit's rate-limit window resets, from <c>X-Bapi-Limit-Reset-Timestamp</c>.
    /// </summary>
    private DateTimeOffset? ResetAt { get; }

    /// <summary>
    ///     The raw <c>Retry-After</c> header: seconds or an HTTP date.
    /// </summary>
    private string? RetryAfter { get; }

    public bool IsRetryable => StatusCode != HttpStatusCode.Forbidden &&
        (StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError ||
            Code is RateLimited or 10000 or 10016 or 170007 ||
            ErrorType is ErrorType.Timeout or ErrorType.NetworkError or ErrorType.UnableToConnect);

    public bool IsInvalidRequest => Code is 10001 or 10029 or 170121 or 170130 || ErrorType is ErrorType.InvalidParameter or ErrorType.MissingParameter;

    public BybitApiException(HttpResult result)
        : base(result.Error?.Message ?? "Bybit request failed.", result.Error?.Exception)
    {
        Code = result is { Error: ServerRateLimitError, ResponseStatusCode: HttpStatusCode.OK } ? RateLimited : result.Error?.Code;
        StatusCode = result.ResponseStatusCode;
        ErrorType = result.Error?.ErrorType ?? ErrorType.Unknown;
        RetryAfter = result.ResponseHeaders?.TryGetValues("Retry-After", out IEnumerable<string>? retryAfter) == true ? retryAfter.FirstOrDefault() : null;

        if (Code == RateLimited &&
            result.ResponseHeaders?.TryGetValues("X-Bapi-Limit-Reset-Timestamp", out IEnumerable<string>? values) == true &&
            long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long milliseconds) &&
            milliseconds is >= 0 and <= 253402300799999)
        {
            ResetAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
    }

    /// <summary>
    ///     The latest moment a retry may start, from the reset timestamp and <c>Retry-After</c>; null when neither is usable.
    /// </summary>
    public DateTimeOffset? RetryAt(DateTimeOffset now)
    {
        DateTimeOffset? retryAt = ResetAt;
        DateTimeOffset? header = null;

        if (RetryAfter is not null && int.TryParse(RetryAfter, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
        {
            header = now.AddSeconds(seconds);
        }
        else if (RetryAfter is not null &&
                 DateTimeOffset.TryParseExact(RetryAfter, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
        {
            header = date;
        }

        if (header is not null && (retryAt is null || header > retryAt))
        {
            retryAt = header;
        }

        return retryAt;
    }
}