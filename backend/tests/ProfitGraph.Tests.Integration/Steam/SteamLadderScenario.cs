using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotPosition;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Infrastructure.Bots.Runtime;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     Runs a Steam Ladder bot in a Worker host with the live role against <see cref="MockSteamMarket" /> (ADR 0022): the
///     real Steam module trades through its journal, confirmations, history and inventory, while candles, warm-up history
///     and display prices are pushed by the test. The clock is the system's, shifted by hand past trade holds; leases last
///     longer than any shift.
/// </summary>
internal sealed class SteamLadderScenario
{
    private const string Symbol = "KZT:730:" + MockSteamMarket.Item;
    private const decimal Price = 500m;

    private static readonly SellerFeeSchedule Fees = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);

    private readonly FakeLiveCandleSource _candles = new FakeLiveCandleSource(SteamVenue.VenueId);
    private readonly FakeLiveMarkPriceSource _prices = new FakeLiveMarkPriceSource(SteamVenue.VenueId);
    private readonly FlatHistorySource _history = new FlatHistorySource(SteamVenue.VenueId, Price);

    public ShiftedClock Clock { get; } = new ShiftedClock();
    public MockSteamMarket Steam { get; }

    public SteamLadderScenario(string steamId)
    {
        Steam = new MockSteamMarket(Clock, steamId);
    }

    /// <summary>
    ///     A Worker host with the live role and Steam trading. The test's sources are registered first, so they serve the
    ///     Steam venue instead of the module's own.
    /// </summary>
    public IHost Worker(Guid credential)
    {
        Dictionary<string, string?> settings = Settings(credential);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return BotTestHost.Create(
            services =>
            {
                services.AddBotRuntime();
                services.AddSingleton<IHistoricalCandleSource>(_history);
                services.AddSingleton<ILiveCandleSource>(_candles);
                services.AddSingleton<ILiveMarkPriceSource>(_prices);
                services.AddSteamVenue(configuration);
                services.AddSteamTrading();
                services.AddHttpClient<SteamHttpTransport>().ConfigurePrimaryHttpMessageHandler(() => Steam);
            },
            Clock,
            settings);
    }

    /// <summary>
    ///     A Ladder bot on the item that should run: its start buys one item at market, then one more item each 10% lower,
    ///     up to three, and sells everything 10% above the average cost.
    /// </summary>
    public static async Task<Bot> RunningLadder(IHost host, Guid owner, Guid credential)
    {
        StrategyDefinition definition = LadderStrategyBlockFactory.Create(Price, 3 * Price, drop: 0.1m, rise: 0.1m);
        using IServiceScope scope = host.Services.CreateScope();
        StrategyInternal strategy = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(
                new CreateStrategyCommand(
                    owner,
                    "Steam ladder",
                    definition.ToInternal(),
                    new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())));
        StrategyVersion version = (await scope.ServiceProvider.GetRequiredService<IStrategyRepository>()
            .GetVersion(new StrategyId(strategy.Id), new StrategyVersionNumber(strategy.CurrentVersion), CancellationToken.None))!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var bot = Bot.Create(
            BotId.New(),
            new UserId(owner),
            new StrategyId(strategy.Id),
            version.Id,
            new ExchangeCredentialId(credential),
            new CandleSeries(SteamVenue.VenueId, new InstrumentSymbol(Symbol), Timeframe.Parse("1h")),
            new BotParameters(definition.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue)),
            3 * Price,
            new BotRiskLimits(4 * Price, 2 * Price),
            new BotFeeRates(limitRate: 0m, marketRate: 0m, now, Fees),
            now);
        bot.Start(version.Id, hasCheckpoint: false, tradingHalted: false, now);
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return bot;
    }

    /// <summary>
    ///     Waits until the eligibility check has made the imported account ready to trade.
    /// </summary>
    public static Task UntilReady(IHost host, Guid credential)
    {
        return RuntimeScenario.Until(
            async () =>
            {
                using IServiceScope scope = host.Services.CreateScope();

                return await scope.ServiceProvider.GetRequiredService<ISteamAccountStore>().Get(credential, CancellationToken.None) is
                    { GuardStatus: SteamGuardStatus.Ready };
            },
            "the eligibility check makes the account ready");
    }

    public static async Task<BotPositionInternal> Position(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotPositionQuery(bot.OwnerId.Value, bot.Id.Value, CycleLimit: 5));
    }

    /// <summary>
    ///     Closes the next hour at the flat price, so the bot decides again with what it holds now.
    /// </summary>
    public async Task NextCandle(IHost host, Bot bot)
    {
        Candle last = (await RuntimeScenario.Checkpoint(host, bot.Id))!.Engine.LastClosedCandle;
        _candles.Push(new Candle(last.OpenTime.AddHours(1), Price, Price, Price, Price, Volume: 1m));
    }

    private static Dictionary<string, string?> Settings(Guid credential)
    {
        // The API's settings carry the credential master key the import encrypted with.
        Dictionary<string, string?> settings = BotApi.Settings().ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        var scenario = new Dictionary<string, string?>
        {
            ["Bots:Runtime:LeaseDuration"] = TimeSpan.FromMinutes(30).ToString(),
            ["Bots:Runtime:RenewInterval"] = TimeSpan.FromMilliseconds(500).ToString(),
            ["Bots:Runtime:PollingInterval"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Bots:Runtime:DispatchInterval"] = TimeSpan.FromMilliseconds(50).ToString(),
            ["Bots:Runtime:ReconcileRetryDelay"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Bots:Runtime:ManualOrderPollInterval"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Venues:Steam:Enabled"] = "true",
            ["Venues:Steam:CommunityBaseAddress"] = MockSteamMarket.Community,
            ["Venues:Steam:LoginBaseAddress"] = MockSteamMarket.Login,
            ["Venues:Steam:ApiBaseAddress"] = MockSteamMarket.Api,
            ["Venues:Steam:MarketDataCredentialId"] = credential.ToString(),
            ["Venues:Steam:WriteSpacing"] = TimeSpan.Zero.ToString(),
            ["Venues:Steam:InitialCooldown"] = TimeSpan.FromSeconds(2).ToString(),
            ["Venues:Steam:OrderWorkerInterval"] = TimeSpan.FromSeconds(1).ToString(),
            ["Venues:Steam:EligibilityInterval"] = TimeSpan.FromSeconds(1).ToString()
        };

        foreach ((string key, string? value) in scenario)
        {
            settings[key] = value;
        }

        return settings;
    }
}