using FluentValidation.Results;

using ProfitGraph.Application.Common.Queries;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.GetCandles;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

namespace ProfitGraph.Tests.Unit.Application.MarketData;

public sealed class ImportCandlesValidatorTests
{
    private readonly VenueRegistry _venues = new VenueRegistry([new TestVenue()]);

    [TestCase("missing", "ABC", "1h", "unknown_venue")]
    [TestCase("test", "", "1h", "invalid_symbol")]
    [TestCase("test", "abc", "1h", "invalid_symbol")]
    [TestCase("test", "ABC", "7m", "unsupported_timeframe")]
    public void RejectsWhatTheVenueDoesNotKnow(string venue, string symbol, string timeframe, string code)
    {
        ValidationResult result = new ImportCandlesCommandValidator(_venues).Validate(new ImportCandlesCommand(venue, symbol, timeframe, RowsNotToEnumerate()));

        result.Errors.Select(error => error.ErrorCode).ShouldBe([code]);
    }

    [Test]
    public void MetadataValidationDoesNotReadTheStream()
    {
        new ImportCandlesCommandValidator(_venues).Validate(new ImportCandlesCommand("test", "ABC", "1h", RowsNotToEnumerate())).IsValid.ShouldBeTrue();
    }

    [Test]
    public void ImportAndQueryShareTheSeriesRules()
    {
        var query = new GetCandlesQuery("test", "abc", "1h", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), Limit: 100);

        new GetCandlesQueryValidator(_venues, new QueryLimitOptions()).Validate(query).Errors.Select(error => error.ErrorCode).ShouldBe(["invalid_symbol"]);
    }

    [TestCase(0)]
    [TestCase(10_001)]
    public void QueryLimitIsBounded(int limit)
    {
        var query = new GetCandlesQuery("test", "ABC", "1h", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), limit);

        new GetCandlesQueryValidator(_venues, new QueryLimitOptions()).Validate(query).IsValid.ShouldBeFalse();
    }

    [Test]
    public void QueryRangeIsUtcAndNotEmpty()
    {
        DateTimeOffset local = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(3));

        new GetCandlesQueryValidator(_venues, new QueryLimitOptions()).Validate(new GetCandlesQuery("test", "ABC", "1h", local, local.AddHours(1), Limit: 1))
            .IsValid.ShouldBeFalse();
        new GetCandlesQueryValidator(_venues, new QueryLimitOptions())
            .Validate(new GetCandlesQuery("test", "ABC", "1h", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Limit: 1))
            .IsValid.ShouldBeFalse();
    }

    [TestCase(arg1: -1, arg2: false)]
    [TestCase(arg1: 0, arg2: true)]
    [TestCase(arg1: 1, arg2: true)]
    public void AcceptsOnlyClosedCandles(int ticksAfterClose, bool expected)
    {
        Candle candle = CandleGenerator.Generate();
        var check = new CandleImportRowCheckInternal(
            new CandleImportRowInternal(candle, LineNumber: 47),
            Timeframe.Parse("1h"),
            candle.OpenTime.AddHours(1).AddTicks(ticksAfterClose));

        ValidationResult result = new CandleImportRowValidator().Validate(check);

        result.IsValid.ShouldBe(expected);

        if (!expected)
        {
            result.Errors.Single().PropertyName.ShouldBe("File.Line[47]");
        }
    }

    private static async IAsyncEnumerable<CandleImportRowInternal> RowsNotToEnumerate()
    {
        await Task.CompletedTask;

        throw new InvalidOperationException("Metadata validation must not enumerate rows.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}