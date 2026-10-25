using System.Globalization;

using Grpc.Core;

using Microsoft.Extensions.Options;

namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     Sends one Invest API call: admits it under the token's service-group limit, attaches the token and the app name as
///     metadata with a deadline, and turns a failure into a <see cref="TInvestRpcException" />. It never retries; a broker
///     refusal for the rate limit holds the group back until the reported reset.
/// </summary>
internal sealed class TInvestTransport(ITInvestCallInvokers invokers, TInvestRequestLimiter limiter, IOptions<TInvestOptions> options)
{
    private static readonly TimeSpan DefaultReset = TimeSpan.FromMinutes(1);

    public async Task<TResponse> Unary<TResponse>(
        TInvestToken token,
        TInvestMethodGroup group,
        Func<CallInvoker, CallOptions, AsyncUnaryCall<TResponse>> call,
        CancellationToken cancellationToken)
    {
        await limiter.Admit(token, group, cancellationToken);
        var metadata = new Metadata
        {
            { "authorization", "Bearer " + token.Value },
            { "x-app-name", options.Value.AppName }
        };

        // gRPC deadlines are wall-clock times, so they come from the system clock, not the application's.
        var callOptions = new CallOptions(metadata, DateTime.UtcNow.Add(options.Value.RequestTimeout), cancellationToken);

        try
        {
            using AsyncUnaryCall<TResponse> pending = call(invokers.Get(), callOptions);

            return await pending.ResponseAsync;
        }
        catch (RpcException error) when (!cancellationToken.IsCancellationRequested)
        {
            if (error.StatusCode == StatusCode.ResourceExhausted)
            {
                limiter.Pause(token, group, Reset(error.Trailers) ?? DefaultReset);
            }

            throw TInvestRpcException.From(error);
        }
    }

    /// <summary>
    ///     Seconds until the limit resets, as the broker's <c>x-ratelimit-reset</c> trailer reports them.
    /// </summary>
    private static TimeSpan? Reset(Metadata trailers)
    {
        string? value = trailers.GetValue("x-ratelimit-reset");

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds is > 0 and <= 3600
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}