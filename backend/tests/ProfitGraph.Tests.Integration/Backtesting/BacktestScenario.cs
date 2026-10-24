using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     A fast SMA crossover strategy on a price wave of the test venue: it trades on many candles of 240 hourly candles.
/// </summary>
internal static class BacktestScenario
{
    private static readonly DateTimeOffset First = FakeHistorySource.DefaultFirst;
    public static readonly DateTimeOffset From = First.AddDays(2);
    public static readonly DateTimeOffset To = First.AddDays(12);

    public static string NewSymbol()
    {
        return "B" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "USDT";
    }

    public static IReadOnlyList<Candle> Wave(Timeframe timeframe, DateTimeOffset from, DateTimeOffset to)
    {
        var candles = new List<Candle>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            long index = (time.UtcTicks - First.UtcTicks) / timeframe.Duration.Ticks;
            decimal open = Price(index - 1);
            decimal close = Price(index);
            candles.Add(new Candle(time, open, Math.Max(open, close) + 1m, Math.Min(open, close) - 1m, close, Volume: 10m));
        }

        return candles;
    }

    public static async Task<Guid> CreateStrategy(ProfitGraphApiFactory application, Guid userId, string name = "Wave SMA")
    {
        using IServiceScope scope = application.Services.CreateScope();
        StrategyInternal created = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(
                new CreateStrategyCommand(
                    userId,
                    name,
                    SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 4).ToInternal(),
                    new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())));

        return created.Id;
    }

    public static object Request(
        Guid strategyId,
        string symbol,
        string venue = TestHistoryVenue.WithHistory,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        Dictionary<string, string>? parameters = null,
        string startingCapital = "1000",
        string outOfSampleFraction = "0.25")
    {
        return new
        {
            strategyId,
            venue,
            symbol,
            timeframe = "1h",
            from = from ?? From,
            to = to ?? To,
            parameters = parameters ?? new Dictionary<string, string> { ["buyAmount"] = "50" },
            startingCapital,
            slippage = "0.001",
            limitFeeRate = "0.001",
            marketFeeRate = "0.001",
            outOfSampleFraction
        };
    }

    public static async Task<Guid> Start(HttpClient client, object request)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/backtests", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
    }

    public static bool Finished(JsonNode job)
    {
        return job["status"]!.GetValue<string>() is "Completed" or "Failed" or "Cancelled";
    }

    private static decimal Price(long index)
    {
        return 100m + (index % 12 < 6 ? index % 12 : 12 - index % 12) * 2m;
    }
}