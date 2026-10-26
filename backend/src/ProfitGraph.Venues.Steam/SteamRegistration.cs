using System.Net;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Confirmations;
using ProfitGraph.Venues.Steam.Enrollment;
using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Inventory;
using ProfitGraph.Venues.Steam.LiveData;
using ProfitGraph.Venues.Steam.MarketData;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Persistence.Repositories;
using ProfitGraph.Venues.Steam.Protection;
using ProfitGraph.Venues.Steam.Session;
using ProfitGraph.Venues.Steam.Trading;

namespace ProfitGraph.Venues.Steam;

public static class SteamRegistration
{
    /// <summary>
    ///     Registers Steam Community Market: its descriptor, backtest defaults, credential fields, its own tables, the
    ///     console import of an existing Steam Guard, account sessions with their queue and protection, and market data
    ///     (median history, item search, rules). Every Steam request stays off while <c>Venues:Steam:Enabled</c> is false.
    /// </summary>
    public static IServiceCollection AddSteamVenue(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IVenueDescriptor, SteamVenue>();
        services.AddSingleton<IVenueExecutionDefaults, SteamExecutionDefaults>();
        services.AddSingleton<IVenueCredentialSchema, SteamCredentialSchema>();
        services.AddScoped<SteamAccountRepository>();
        services.AddScoped<ISteamAccountStore>(provider => provider.GetRequiredService<SteamAccountRepository>());
        services.AddSingleton<ISteamBotAccounts, SteamBotAccounts>();
        services.AddScoped<IOperatorCommand, SteamImportCommand>();
        services.ConfigureRunner(runner => runner.ScanIn(typeof(SteamRegistration).Assembly).For.Migrations());
        services.AddHttpClient<SteamHttpTransport>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All
            })
            .RemoveAllLoggers();
        services.AddSingleton<SteamRequestScheduler>();
        services.AddSingleton<ISteamAccountProtectionStore, SteamAccountProtectionRepository>();
        services.AddSingleton<SteamAccountProtection>();
        services.AddSingleton<IVenueAccountProtection>(provider => provider.GetRequiredService<SteamAccountProtection>());
        services.AddSingleton<IVenueAuthenticatorCodes, SteamAuthenticatorCodes>();
        services.AddSingleton<IVenueAccountStatus, SteamAccountStatus>();
        services.AddSingleton<ISteamEnrollmentStore, SteamEnrollmentRepository>();
        services.AddSingleton<SteamEnrollmentClient>();
        services.AddSingleton<IVenueCredentialEnrollment, SteamCredentialEnrollment>();
        services.AddSingleton<SteamAccountSessions>();
        services.AddSingleton<SteamMarketDataAccount>();
        services.AddSingleton<SteamInventoryReader>();
        services.AddSingleton<ISteamOrderJournal, SteamOrderJournalRepository>();
        services.AddSingleton<SteamBuyOrderClient>();
        services.AddSingleton<SteamOrderUpdates>();
        services.AddSingleton<SteamBuyOrders>();
        services.AddSingleton<SteamListingClient>();
        services.AddSingleton<ISteamMarketHistoryStore, SteamMarketHistoryRepository>();
        services.AddSingleton<SteamMarketHistory>();
        services.AddSingleton<ISteamMarketAttributions, SteamMarketAttributionRepository>();
        services.AddSingleton<ISteamOwnedAssets, SteamOwnedAssetsFromHistory>();
        services.AddSingleton<SteamSells>();
        services.AddSingleton<SteamExecutions>();
        services.AddSingleton<ISteamOrderWaiting, SteamOrderWaiting>();
        services.AddSingleton<SteamSellSettlement>();
        services.AddSingleton<SteamConfirmationClient>();
        services.AddSingleton<SteamConfirmations>();
        services.AddSingleton<IHistoricalCandleSource, SteamHistoricalCandleSource>();
        services.AddSingleton<IInstrumentRulesSource, SteamInstrumentRulesSource>();
        services.AddSingleton<IInstrumentCatalog, SteamInstrumentCatalog>();
        services.AddSingleton<ILiveCandleSource, SteamLiveCandleSource>();
        services.AddSingleton<ILiveMarkPriceSource, SteamLiveMarkPriceSource>();
        services.AddSingleton<IVenueFeeRateReader, SteamFeeRateReader>();
        services.AddSingleton<IAccountBalanceReader, SteamAccountBalanceReader>();
        services.AddSingleton<IVenueBotSettings, SteamBotSettingsSchema>();
        services.AddOptions<SteamOptions>()
            .Bind(configuration.GetSection(SteamOptions.SectionName))
            .Validate(options => options.IsValid(), "Venues:Steam needs the official HTTPS origins of Community, login and Web API when it is enabled.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    ///     Registers Steam order execution: the order gateway, the worker that moves accounts with open orders along and the
    ///     worker that checks accounts awaiting their eligibility check. Only a
    ///     Worker with the live role calls it, and only when <c>Live:Trading:Enabled</c> is true; every committed setting keeps
    ///     it false, and <c>Venues:Steam:Enabled</c> still holds back every request (ADR 0022).
    /// </summary>
    public static IServiceCollection AddSteamTrading(this IServiceCollection services)
    {
        services.AddSingleton<IOrderGateway, SteamOrderGateway>();
        services.AddSingleton<IOrderReadiness, SteamOrderReadiness>();
        services.AddHostedService<SteamOrderWorker>();
        services.AddSingleton<SteamEligibility>();
        services.AddHostedService<SteamEligibilityWorker>();

        return services;
    }
}