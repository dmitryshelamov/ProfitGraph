using NSubstitute;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Unit.Common;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

namespace ProfitGraph.Tests.Unit.Application.MarketData;

public sealed class ImportCandlesCommandHandlerTests
{
    private static readonly FixedClock Clock = new FixedClock(new DateTimeOffset(year: 2026, month: 1, day: 2, hour: 0, minute: 0, second: 0, TimeSpan.Zero));
    private ICandleRepository _repository = null!;

    [SetUp]
    public void CreateRepository()
    {
        _repository = Substitute.For<ICandleRepository>();
    }

    [Test]
    public async Task WritesFirstBatchBeforeReadingNext_AndHandlesTailAndDuplicates()
    {
        CandleImportInternal result = await Handler().Handle(Command(Stream()), CancellationToken.None);

        result.ShouldSatisfyAllConditions(
            () => result.Accepted.ShouldBe(3),
            () => result.Venue.ShouldBe("test"),
            () => result.Symbol.ShouldBe("ABC"),
            () => result.Timeframe.ShouldBe("1h"),
            () => result.From.ShouldBe(DateTimeOffset.UnixEpoch),
            () => result.To.ShouldBe(DateTimeOffset.UnixEpoch.AddHours(3)));
        ImportedBatchSizes().ShouldBe([2, 1]);
        await _repository.DidNotReceiveWithAnyArgs().Get(null!, from: default, to: default, limit: 0, CancellationToken.None);

        async IAsyncEnumerable<CandleImportRowInternal> Stream()
        {
            for (var index = 0; index < 3; index++)
            {
                if (index == 2)
                {
                    ImportedBatchSizes().ShouldBe([2]);
                }

                yield return new CandleImportRowInternal(CandleGenerator.Generate(DateTimeOffset.UnixEpoch.AddHours(index)), index + 2);
            }

            yield return new CandleImportRowInternal(CandleGenerator.Generate(), LineNumber: 5);
            await Task.CompletedTask;
        }
    }

    [Test]
    public async Task StoresTheSeriesTheVenueResolved()
    {
        await Handler().Handle(Command(Rows(CandleGenerator.Generate())), CancellationToken.None);

        await _repository.Received(1)
            .Import(
                new CandleSeries(new VenueId("test"), new InstrumentSymbol("ABC"), Timeframe.Parse("1h")),
                Arg.Any<IReadOnlyList<Candle>>(),
                Clock.GetUtcNow(),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CapsErrorsAcrossBatchesAndCountsAllInvalidRows()
    {
        var error = await Should.ThrowAsync<ApplicationValidationException>(() => Handler().Handle(Command(InvalidRows()), CancellationToken.None));

        error.Errors.Count.ShouldBe(3);
        error.Errors.First().Field.ShouldBe("File.Line[2]");
        error.Errors.Last().Message.ShouldContain("6 invalid candle rows");

        static async IAsyncEnumerable<CandleImportRowInternal> InvalidRows()
        {
            for (var index = 0; index < 6; index++)
            {
                yield return new CandleImportRowInternal(CandleGenerator.Generate() with { High = 1 }, index + 2);
            }

            await Task.CompletedTask;
        }
    }

    [Test]
    public async Task RejectsConflictingDuplicateFromFlushedBatchWithoutReadingDatabase()
    {
        Candle first = CandleGenerator.Generate();

        var error = await Should.ThrowAsync<ApplicationValidationException>(() => Handler()
            .Handle(
                Command(Rows(first, CandleGenerator.Generate(DateTimeOffset.UnixEpoch.AddHours(1)), first with { Close = 106 })),
                CancellationToken.None));

        error.Errors.Single().Field.ShouldBe("File.Line[4]");
        error.Errors.Single().Message.ShouldContain("Conflicting candles");
        ImportedBatchSizes().ShouldBe([2]);
        await _repository.DidNotReceiveWithAnyArgs().Get(null!, from: default, to: default, limit: 0, CancellationToken.None);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public async Task StopsWritingAfterFirstErrorButContinuesValidation(int firstInvalidIndex)
    {
        Candle[] candles = [.. Enumerable.Range(start: 0, count: 6).Select(index => CandleGenerator.Generate(DateTimeOffset.UnixEpoch.AddHours(index)))];
        candles[firstInvalidIndex] = candles[firstInvalidIndex] with { High = 1 };
        candles[5] = candles[5] with { High = 1 };

        var error = await Should.ThrowAsync<ApplicationValidationException>(() => Handler().Handle(Command(Rows(candles)), CancellationToken.None));

        error.Errors.Select(item => item.Field).ShouldBe([$"File.Line[{firstInvalidIndex + 2}]", "File.Line[7]"]);
        ImportedBatchSizes().ShouldBe(firstInvalidIndex >= 2 ? [2] : []);
    }

    [Test]
    public async Task StillDetectsConflictingDuplicatesAfterFirstError()
    {
        Candle candle = CandleGenerator.Generate();

        var error = await Should.ThrowAsync<ApplicationValidationException>(() => Handler()
            .Handle(
                Command(Rows(candle with { High = 1 }, candle, candle with { Close = 106 })),
                CancellationToken.None));

        error.Errors.Last().Message.ShouldContain("Conflicting candles");
        await _repository.DidNotReceiveWithAnyArgs().Import(null!, null!, importedAt: default, CancellationToken.None);
    }

    [TestCase(arg1: 0, arg2: 2)]
    [TestCase(arg1: 1, arg2: 2)]
    [TestCase(arg1: 3, arg2: 2)]
    [TestCase(arg1: 3, arg2: 1)]
    public async Task PreservesDataErrorsAndReadFailureWithinSharedLimit(int invalidCount, int maxDetailedErrors)
    {
        var error = await Should.ThrowAsync<ApplicationValidationException>(() =>
            Handler(maxDetailedErrors).Handle(Command(FailingStream()), CancellationToken.None));

        error.Errors.Count.ShouldBe(Math.Min(invalidCount, maxDetailedErrors - 1) + 1 + (invalidCount > 0 ? 1 : 0));
        error.Errors.ShouldContain(item => item.Code == "invalid_csv" && item.Field == $"File.Line[{invalidCount + 2}]");

        if (invalidCount > 0)
        {
            error.Errors.Last().Message.ShouldContain($"{invalidCount} invalid candle rows found before reading stopped");

            if (maxDetailedErrors > 1)
            {
                error.Errors.First().Field.ShouldBe("File.Line[2]");
            }
        }

        await _repository.DidNotReceiveWithAnyArgs().Import(null!, null!, importedAt: default, CancellationToken.None);

        async IAsyncEnumerable<CandleImportRowInternal> FailingStream()
        {
            for (var index = 0; index < invalidCount; index++)
            {
                yield return new CandleImportRowInternal(CandleGenerator.Generate() with { High = 1 }, index + 2);
            }

            await Task.CompletedTask;

            throw new ApplicationValidationException([new InputError("invalid_csv", $"File.Line[{invalidCount + 2}]", "Invalid decimal.")]);
        }
    }

    [Test]
    public async Task RejectsEmptyStream()
    {
        await Should.ThrowAsync<ApplicationValidationException>(() => Handler().Handle(Command(Rows()), CancellationToken.None));
    }

    [Test]
    public async Task RejectsMoreRowsThanTheLimit()
    {
        Candle[] candles = [.. Enumerable.Range(start: 0, count: 7).Select(index => CandleGenerator.Generate(DateTimeOffset.UnixEpoch.AddHours(index)))];

        var error = await Should.ThrowAsync<ApplicationValidationException>(() => Handler().Handle(Command(Rows(candles)), CancellationToken.None));

        error.Errors.ShouldContain(item => item.Message.Contains("at most 6 candles"));
    }

    private ImportCandlesCommandHandler Handler(int maxDetailedErrors = 2)
    {
        var options = new CsvImportOptions
        {
            BatchSize = 2,
            MaxRows = 6,
            MaxFileSizeBytes = 1024,
            MaxRequestSizeBytes = 2048,
            MaxRowLength = 128,
            MaxDetailedErrors = maxDetailedErrors
        };

        return new ImportCandlesCommandHandler(new VenueRegistry([new TestVenue()]), _repository, Clock, new CandleImportRowValidator(), options);
    }

    private static ImportCandlesCommand Command(IAsyncEnumerable<CandleImportRowInternal> rows)
    {
        return new ImportCandlesCommand("test", "ABC", "1h", rows);
    }

    private static async IAsyncEnumerable<CandleImportRowInternal> Rows(params Candle[] candles)
    {
        for (var index = 0; index < candles.Length; index++)
        {
            yield return new CandleImportRowInternal(candles[index], index + 2);
        }

        await Task.CompletedTask;
    }

    private int[] ImportedBatchSizes()
    {
        return
        [
            .. _repository.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(ICandleRepository.Import))
                .Select(call => ((IReadOnlyList<Candle>)call.GetArguments()[1]!).Count)
        ];
    }
}