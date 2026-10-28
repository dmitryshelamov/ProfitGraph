using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;

using MediatR;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.BootstrapOwner;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.GetUser;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Api.Common.Authentication;

/// <summary>
///     Cookie sessions for invite-only accounts: every API endpoint requires a signed-in active account unless it opts out,
///     sign-in is rate limited per client address and locked out per login after failures, blocking an account ends its
///     sessions and realtime connections, and the first Owner can come from private configuration.
/// </summary>
internal static class AuthenticationSetup
{
    internal const string SignInPolicy = "sign-in";
    internal const string OwnerRole = nameof(UserRole.Owner);

    internal static void AddProfitGraphAuthentication(this WebApplicationBuilder builder)
    {
        string? keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];

        if (!string.IsNullOrWhiteSpace(keyDirectory))
        {
            builder.Services.AddDataProtection().SetApplicationName("ProfitGraph").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        }

        builder.Services.AddOptions<AuthOptions>()
            .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
            .Validate(options => options.IsValid(), "Auth needs positive limits and a maximum lockout no shorter than the first.")
            .ValidateOnStart();
        bool insecureCookiesAllowed = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "profitgraph.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = insecureCookiesAllowed ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context => Status(context.Response, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => Status(context.Response, StatusCodes.Status403Forbidden);
                options.Events.OnValidatePrincipal = RejectInactiveAccounts;
            });
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((cookie, auth) => cookie.ExpireTimeSpan = auth.Value.SessionLifetime);
        builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                SignInPolicy,
                context =>
                {
                    AuthOptions auth = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;

                    return RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = auth.SignInPermitLimit, Window = auth.SignInWindow, QueueLimit = 0 });
                });
        });
        builder.Services.AddSingleton<SignInThrottle>();
        builder.Services.AddSingleton<AccountConnections>();
        builder.Services.AddSingleton<ActiveAccountHubFilter>();
        builder.Services.Configure<HubOptions>(options => options.AddFilter<ActiveAccountHubFilter>());
        ConfigureKnownProxies(builder);
    }

    internal static void UseProfitGraphAuthentication(this WebApplication app)
    {
        if (app.Configuration.GetSection("ReverseProxy:KnownProxies").GetChildren().Any())
        {
            app.UseForwardedHeaders();
        }

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
    }

    internal static void BootstrapOwner(this WebApplication app)
    {
        string? login = app.Configuration["Auth:BootstrapOwner:Login"];
        string? password = app.Configuration["Auth:BootstrapOwner:Password"];

        // appsettings.json carries empty placeholders; the real values come from user secrets or the environment.
        if (string.IsNullOrEmpty(login) && string.IsNullOrEmpty(password))
        {
            return;
        }

        using IServiceScope scope = app.Services.CreateScope();
        bool created = scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new BootstrapOwnerCommand(login ?? string.Empty, password ?? string.Empty))
            .GetAwaiter()
            .GetResult();

        if (created)
        {
            app.Logger.LogInformation("Bootstrap Owner account created.");
        }
    }

    internal static ClaimsPrincipal Principal(UserInternal user)
    {
        Claim[] claims =
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")), new Claim(ClaimTypes.Name, user.Login), new Claim(ClaimTypes.Role, user.Role.ToString())
        ];

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    internal static Guid? UserId(ClaimsPrincipal principal)
    {
        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;
    }

    private static async Task RejectInactiveAccounts(CookieValidatePrincipalContext context)
    {
        Guid? id = context.Principal is null ? null : UserId(context.Principal);
        UserInternal? user = id is null
            ? null
            : await context.HttpContext.RequestServices.GetRequiredService<ISender>().Send(new GetUserQuery(id.Value), context.HttpContext.RequestAborted);

        if (user is not { Status: UserStatus.Active })
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static Task Status(HttpResponse response, int status)
    {
        response.StatusCode = status;

        return Task.CompletedTask;
    }

    private static void ConfigureKnownProxies(WebApplicationBuilder builder)
    {
        IConfigurationSection[] proxies = [.. builder.Configuration.GetSection("ReverseProxy:KnownProxies").GetChildren()];

        if (proxies.Length == 0)
        {
            return;
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (IConfigurationSection proxy in proxies)
            {
                options.KnownProxies.Add(
                    IPAddress.TryParse(proxy.Value, out IPAddress? address)
                        ? address
                        : throw new InvalidOperationException("ReverseProxy:KnownProxies contains an invalid IP address."));
            }
        });
    }
}