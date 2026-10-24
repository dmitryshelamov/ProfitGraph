using Microsoft.Extensions.Logging;

using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     Runs one Bybit request within a total budget: per-attempt timeout, exponential retries of transient failures,
///     waiting until Bybit's rate-limit reset when Bybit says when, and a circuit breaker. A reset that would end after
///     the budget fails at once instead of waiting. It uses the system clock, never the application's, because Bybit's
///     reset times are real time.
/// </summary>
internal sealed class BybitResilienceExecutor
{
    private static readonly ResiliencePropertyKey<long> StartedAt = new ResiliencePropertyKey<long>("bybit.started-at");
    private static readonly ResiliencePropertyKey<BybitApiException?> ResetError = new ResiliencePropertyKey<BybitApiException?>("bybit.reset-error");
    private static readonly ResiliencePropertyKey<DateTimeOffset?> ResetDeadline = new ResiliencePropertyKey<DateTimeOffset?>("bybit.reset-deadline");

    private readonly TimeProvider _clock;
    private readonly ResiliencePipeline _pipeline;
    private readonly TimeSpan _totalTimeout;

    public BybitResilienceExecutor(TimeProvider clock, BybitOptions options, ILogger<BybitResilienceExecutor> logger)
    {
        _clock = clock;
        _totalTimeout = options.TotalTimeout;
        _pipeline = new ResiliencePipelineBuilder { TimeProvider = clock }
            .AddTimeout(options.TotalTimeout)
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    Delay = options.RetryBaseDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    ShouldHandle = arguments => new ValueTask<bool>(ShouldRetry(arguments.Outcome.Exception, arguments.Context)),
                    DelayGenerator = arguments => new ValueTask<TimeSpan?>(
                        arguments.Context.Properties.GetValue(ResetDeadline, defaultValue: null) is { } reset ? RoundUp(reset - clock.GetUtcNow()) : null),
                    OnRetry = arguments =>
                    {
                        logger.LogWarning(arguments.Outcome.Exception, "Bybit retry {Attempt} after {Delay}.", arguments.AttemptNumber + 1, arguments.RetryDelay);

                        return default;
                    }
                })
            .AddCircuitBreaker(
                new CircuitBreakerStrategyOptions
                {
                    FailureRatio = options.CircuitFailureRatio,
                    MinimumThroughput = options.CircuitMinimumThroughput,
                    SamplingDuration = options.CircuitSamplingDuration,
                    BreakDuration = options.CircuitBreakDuration,
                    ShouldHandle = arguments => new ValueTask<bool>(IsTransient(arguments.Outcome.Exception))
                })
            .AddTimeout(options.AttemptTimeout)
            .Build();
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        ResilienceContext context = ResilienceContextPool.Shared.Get(cancellationToken);

        try
        {
            context.Properties.Set(StartedAt, _clock.GetTimestamp());
            context.Properties.Set(ResetError, value: null);
            context.Properties.Set(ResetDeadline, value: null);

            return await _pipeline.ExecuteAsync(
                async execution =>
                {
                    await WaitForReset(execution);

                    return await action(execution.CancellationToken);
                },
                context);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <summary>
    ///     Retries a transient failure. A rate limit with a known reset is retried only if the reset comes inside the budget.
    /// </summary>
    private bool ShouldRetry(Exception? exception, ResilienceContext context)
    {
        if (!IsTransient(exception))
        {
            return false;
        }

        if (exception is BybitApiException error && error.RetryAt(_clock.GetUtcNow()) is { } reset)
        {
            context.Properties.Set(ResetError, error);
            context.Properties.Set(ResetDeadline, reset);

            return RoundUp(reset - _clock.GetUtcNow()) < Remaining(context);
        }

        context.Properties.Set(ResetError, value: null);
        context.Properties.Set(ResetDeadline, value: null);

        return true;
    }

    /// <summary>
    ///     Waits until the reset Bybit reported before the next attempt; the wait can be cancelled and never exceeds the budget.
    /// </summary>
    private async Task WaitForReset(ResilienceContext context)
    {
        if (context.Properties.GetValue(ResetError, defaultValue: null) is not { } error ||
            context.Properties.GetValue(ResetDeadline, defaultValue: null) is not { } reset)
        {
            return;
        }

        while (true)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            TimeSpan delay = RoundUp(reset - _clock.GetUtcNow());

            if (delay >= Remaining(context))
            {
                throw error;
            }

            if (delay == TimeSpan.Zero)
            {
                context.Properties.Set(ResetError, value: null);
                context.Properties.Set(ResetDeadline, value: null);

                return;
            }

            await Task.Delay(delay, _clock, context.CancellationToken);
        }
    }

    private TimeSpan Remaining(ResilienceContext context)
    {
        return _totalTimeout - _clock.GetElapsedTime(context.Properties.GetValue(StartedAt, defaultValue: 0L));
    }

    private static TimeSpan RoundUp(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        long milliseconds = delay.Ticks / TimeSpan.TicksPerMillisecond + (delay.Ticks % TimeSpan.TicksPerMillisecond == 0 ? 0 : 1);

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static bool IsTransient(Exception? exception)
    {
        return exception is BybitApiException { IsRetryable: true } or TimeoutRejectedException or TimeoutException or HttpRequestException or IOException;
    }
}