using FluentValidation;
using FluentValidation.Results;

using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.ImportCandles;

/// <summary>
///     Checks the streamed rows and stores them in batches. Any invalid row fails the command, and the transaction of the
///     pipeline rolls back every batch already written.
/// </summary>
internal sealed class ImportCandlesCommandHandler(
    VenueRegistry venues,
    ICandleRepository repository,
    TimeProvider clock,
    IValidator<CandleImportRowCheckInternal> rowValidator,
    CsvImportOptions options) : IRequestHandler<ImportCandlesCommand, CandleImportInternal>
{
    public async Task<CandleImportInternal> Handle(ImportCandlesCommand request, CancellationToken cancellationToken)
    {
        CandleSeries series = venues.ResolveSeries(request.Venue, request.Symbol, request.Timeframe);
        DateTimeOffset now = clock.GetUtcNow();
        var batch = new Dictionary<DateTimeOffset, Candle>();
        var seen = new Dictionary<DateTimeOffset, Candle>();
        var errors = new List<InputError>();
        var invalidRows = 0;
        var rowCount = 0;
        var from = DateTimeOffset.MaxValue;
        var to = DateTimeOffset.MinValue;

        await using IAsyncEnumerator<CandleImportRowInternal> rows = request.Candles.GetAsyncEnumerator(cancellationToken);

        while (await ReadNext(rows, errors, invalidRows, cancellationToken))
        {
            CandleImportRowInternal row = rows.Current;

            if (++rowCount > options.MaxRows)
            {
                throw ReadFailure(errors, invalidRows, [new InputError("invalid_csv", "File", $"CSV must contain at most {options.MaxRows} candles.")]);
            }

            ValidationResult result = await rowValidator.ValidateAsync(new CandleImportRowCheckInternal(row, series.Timeframe, now), cancellationToken);
            InputError? error = result.IsValid ? null : new InputError("invalid_candle", result.Errors[0].PropertyName, result.Errors[0].ErrorMessage);

            if (error is null)
            {
                Candle candle = row.Candle;

                if (seen.TryGetValue(candle.OpenTime, out Candle? previous))
                {
                    if (previous != candle)
                    {
                        error = new InputError("invalid_candle", $"File.Line[{row.LineNumber}]", $"Conflicting candles have the same open time: {candle.OpenTime:O}.");
                    }
                }
                else
                {
                    seen.Add(candle.OpenTime, candle);

                    if (invalidRows == 0)
                    {
                        batch.Add(candle.OpenTime, candle);
                    }

                    from = candle.OpenTime < from ? candle.OpenTime : from;
                    DateTimeOffset closeTime = candle.OpenTime.Add(series.Timeframe.Duration);
                    to = closeTime > to ? closeTime : to;
                }
            }

            if (error is not null)
            {
                batch.Clear();

                if (++invalidRows <= options.MaxDetailedErrors)
                {
                    errors.Add(error);
                }
            }

            if (invalidRows == 0 && batch.Count == options.BatchSize)
            {
                await Store(series, batch, now, cancellationToken);
            }
        }

        if (rowCount == 0)
        {
            errors.Add(new InputError("invalid_csv", "File", "CSV must contain at least one candle."));
        }

        if (invalidRows > options.MaxDetailedErrors)
        {
            errors.Add(new InputError("invalid_candle", "File", $"{invalidRows} invalid candle rows in total; only the first {options.MaxDetailedErrors} are shown."));
        }

        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }

        if (batch.Count > 0)
        {
            await Store(series, batch, now, cancellationToken);
        }

        return new CandleImportInternal(series.Venue.Value, series.Symbol.Value, series.Timeframe.Key, seen.Count, from, to);
    }

    /// <summary>
    ///     Stores a batch unless one of its candles lies in a market closure the venue reported (ADR 0023, TC1) or would
    ///     replace a stored candle without trades (TC2): such a file contradicts the cache, and only an explicit correction may
    ///     replace a candle without trades.
    /// </summary>
    private async Task Store(CandleSeries series, Dictionary<DateTimeOffset, Candle> batch, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Candle[] candles = [.. batch.Values.OrderBy(candle => candle.OpenTime)];
        DateTimeOffset end = candles[^1].OpenTime + series.Timeframe.Duration;
        IReadOnlyList<CandleRangeInternal> closures = await repository.GetClosures(series, candles[0].OpenTime, end, cancellationToken);
        Candle? closed = candles.FirstOrDefault(candle => closures.Any(closure => closure.From <= candle.OpenTime && candle.OpenTime < closure.To));

        if (closed is not null)
        {
            throw new ApplicationValidationException(
                [new InputError("invalid_candle", "File", $"The candle at {closed.OpenTime:O} lies in a market closure the venue reported.")]);
        }

        DateTimeOffset? withoutTrades = await repository.FindWithoutTrades(series, [.. batch.Keys], cancellationToken);

        if (withoutTrades is not null)
        {
            throw new ApplicationValidationException(
                [new InputError("invalid_candle", "File", $"The candle at {withoutTrades:O} replaces a stored candle without trades.")]);
        }

        await repository.Import(series, candles, now, cancellationToken);
        batch.Clear();
    }

    private async Task<bool> ReadNext(
        IAsyncEnumerator<CandleImportRowInternal> rows,
        IReadOnlyList<InputError> errors,
        int invalidRows,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await rows.MoveNextAsync();
        }
        catch (ApplicationValidationException exception)
        {
            throw ReadFailure(errors, invalidRows, exception.Errors);
        }
    }

    /// <summary>
    ///     Reports the reason reading stopped together with the row errors found so far, keeping room for that reason.
    /// </summary>
    private ApplicationValidationException ReadFailure(IReadOnlyList<InputError> errors, int invalidRows, IReadOnlyCollection<InputError> readErrors)
    {
        List<InputError> combined = errors.Take(options.MaxDetailedErrors - 1).ToList();
        int shownInvalidRows = combined.Count;
        combined.AddRange(readErrors.Take(options.MaxDetailedErrors - combined.Count));

        if (invalidRows > 0)
        {
            combined.Add(
                new InputError(
                    "invalid_candle",
                    "File",
                    $"{invalidRows} invalid candle rows found before reading stopped; {shownInvalidRows} are shown. The remaining input was not validated."));
        }

        return new ApplicationValidationException(combined);
    }
}