using MediatR;

using Microsoft.AspNetCore.SignalR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.GetUser;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Api.Common.Authentication;

/// <summary>
///     Keeps realtime connections to active accounts (ADR 0043): every hub connection is registered in
///     <see cref="AccountConnections" /> so blocking closes it, and the account is checked again when the connection opens
///     and on every hub method call, so a connection that a block missed, for example on another API instance, ends on its
///     next call.
/// </summary>
internal sealed class ActiveAccountHubFilter(AccountConnections connections) : IHubFilter
{
    internal const string InactiveAccount = "account_inactive";

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        HubCallerContext connection = invocationContext.Context;

        if (!await IsActive(UserId(connection), invocationContext.ServiceProvider, connection.ConnectionAborted))
        {
            connection.Abort();

            throw new HubException(InactiveAccount);
        }

        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        HubCallerContext connection = context.Context;
        Guid? userId = UserId(connection);

        if (userId is not null)
        {
            connections.Add(userId.Value, connection);
        }

        try
        {
            // Checked after registering: a block that commits meanwhile either finds this connection or is seen here.
            if (!await IsActive(userId, context.ServiceProvider, connection.ConnectionAborted))
            {
                throw new HubException(InactiveAccount);
            }

            await next(context);
        }
        catch
        {
            if (userId is not null)
            {
                connections.Remove(userId.Value, connection.ConnectionId);
            }

            throw;
        }
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        if (UserId(context.Context) is { } userId)
        {
            connections.Remove(userId, context.Context.ConnectionId);
        }

        await next(context, exception);
    }

    private static Guid? UserId(HubCallerContext connection)
    {
        return connection.User is { } user ? AuthenticationSetup.UserId(user) : null;
    }

    private static async Task<bool> IsActive(Guid? userId, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return false;
        }

        UserInternal? user = await services.GetRequiredService<ISender>().Send(new GetUserQuery(userId.Value), cancellationToken);

        return user is { Status: UserStatus.Active };
    }
}