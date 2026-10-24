using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.MarketData;

[NonParallelizable]
public sealed class StreamingCandleImportTests
{
    [Test]
    public async Task CancellationAfterWrittenBatchRollsBackTransaction()
    {
        await using var application = new ProfitGraphApiFactory(new TestClock());
        using IServiceScope scope = application.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var repository = scope.ServiceProvider.GetRequiredService<ICandleRepository>();
        CandleSeries series = scope.ServiceProvider.GetRequiredService<VenueRegistry>().ResolveSeries("bybit", "BTCUSDT", "1h");
        using var cancellation = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(() => sender.Send(new ImportCandlesCommand("bybit", "BTCUSDT", "1h", Rows()), cancellation.Token));

        (await repository.Get(series, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1000), limit: 1001, CancellationToken.None)).ShouldBeEmpty();

        async IAsyncEnumerable<CandleImportRowInternal> Rows()
        {
            for (var index = 0; index < 1000; index++)
            {
                yield return new CandleImportRowInternal(Candle(DateTimeOffset.UnixEpoch.AddHours(index)), index + 2);
            }

            IReadOnlyList<Candle> written = await repository.Get(
                series,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddHours(1000),
                limit: 1001,
                CancellationToken.None);
            written.Count.ShouldBe(1000);
            await cancellation.CancelAsync();
            cancellation.Token.ThrowIfCancellationRequested();
        }
    }

    private static Candle Candle(DateTimeOffset openTime)
    {
        return new Candle(openTime, Open: 100, High: 110, Low: 90, Close: 105, Volume: 1);
    }
}