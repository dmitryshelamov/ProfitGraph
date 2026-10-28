using FluentValidation;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application;
using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.EnrollCredential;
using ProfitGraph.Application.Strategies.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Accounts.Repositories;
using ProfitGraph.Infrastructure.Accounts.Security;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Bots;
using ProfitGraph.Infrastructure.Common.Messaging;
using ProfitGraph.Infrastructure.Common.NodeTypes;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Infrastructure.MarketData;
using ProfitGraph.Infrastructure.Notifications;
using ProfitGraph.Infrastructure.Notifications.Repositories;
using ProfitGraph.Infrastructure.Optimization;
using ProfitGraph.Infrastructure.Strategies.Repositories;
using ProfitGraph.Infrastructure.Strategies.Repositories.Converters;

namespace ProfitGraph.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    ///     Registers the Application pipeline (validation outside the transaction, then the transaction) and PostgreSQL.
    /// </summary>
    public static IServiceCollection AddProfitGraphInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(mediatr =>
        {
            mediatr.RegisterServicesFromAssembly(typeof(ApplicationAssembly).Assembly);
            mediatr.AddOpenBehavior(typeof(ValidationBehavior<,>));
            mediatr.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });
        services.AddValidatorsFromAssembly(typeof(ApplicationAssembly).Assembly, includeInternalTypes: true);
        services.AddPostgres(configuration);
        services.AddOutbox(configuration);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddScoped<IExchangeCredentialRepository, ExchangeCredentialRepository>();
        services.AddScoped<IAccountSnapshotRepository, AccountSnapshotRepository>();
        services.AddScoped<IAccountBalanceStatusRepository, AccountBalanceStatusRepository>();
        services.AddScoped<IAccountIdentityRepository, AccountIdentityRepository>();
        services.AddScoped<ITelegramLinkRepository, TelegramLinkRepository>();
        services.AddScoped<ITelegramPollerLeaseRepository, TelegramPollerLeaseRepository>();
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .Validate(options => options.IsValid(), "Telegram needs a bot token, an absolute base address and positive delays when it is enabled.")
            .ValidateOnStart();
        services.AddOptions<SecretsOptions>().Bind(configuration.GetSection(SecretsOptions.SectionName));
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddScoped<CredentialEnrollments>();
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        services.AddScoped<IStrategyVersionReader, StrategyVersionReader>();
        services.AddSingleton<StrategySnapshotDbConverter>();
        services.AddNodeTypes();
        services.AddMarketData(configuration);
        services.AddBackgroundJobs(configuration);
        services.AddBacktesting(configuration);
        services.AddOptimization(configuration);
        services.AddBots(configuration);

        return services;
    }
}