using MediatR;

using Microsoft.AspNetCore.SignalR;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Optimization.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.GetOptimizationCursor;

namespace ProfitGraph.Api.Optimization.Realtime;

/// <summary>
///     <c>/ws/optimizations</c> (stage 4 plan, S4-12): a client subscribes to its own optimizations and receives
///     <c>OptimizationProgress</c> for each stored trial, in delivery order, and <c>OptimizationFinished</c> once.
///     Messages can be missed, for example across a reconnect; the client reads trials after its cursor over HTTP and
///     drops sequences it already has.
/// </summary>
public sealed class OptimizationHub(ISender sender, OptimizationProgressSubscriptions subscriptions) : Hub
{
    public const string Path = "/ws/optimizations";

    public async Task<OptimizationCursorResponse> Subscribe(Guid optimizationId)
    {
        Guid userId = (Context.User is { } user ? AuthenticationSetup.UserId(user) : null) ?? throw new HubException("Sign in to follow optimizations.");
        var query = new GetOptimizationCursorQuery(userId, optimizationId);

        try
        {
            // Checks ownership before the connection joins the optimization's group.
            await sender.Send(query, Context.ConnectionAborted);
        }
        catch (ResourceNotFoundException exception)
        {
            throw new HubException(exception.Code);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, OptimizationProgressSubscriptions.Group(optimizationId), Context.ConnectionAborted);

        // Read after joining: anything stored later is sent to this connection, so the cursor leaves no gap.
        OptimizationCursorInternal cursor = await sender.Send(query, Context.ConnectionAborted);

        if (!cursor.IsFinished)
        {
            subscriptions.Watch(optimizationId, cursor.LastSequence);
        }

        return new OptimizationCursorResponse(cursor.LastSequence, cursor.Status);
    }

    public Task Unsubscribe(Guid optimizationId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, OptimizationProgressSubscriptions.Group(optimizationId), Context.ConnectionAborted);
    }
}