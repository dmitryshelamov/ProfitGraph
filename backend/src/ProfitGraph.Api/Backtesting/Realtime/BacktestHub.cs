using MediatR;

using Microsoft.AspNetCore.SignalR;

using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.GetBacktestCursor;
using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Api.Backtesting.Realtime;

/// <summary>
///     <c>/ws/backtests</c> (stage 3 plan, S3-9): a client subscribes to its own runs and receives <c>BacktestProgress</c>
///     after each committed batch and <c>BacktestFinished</c> once. Messages can be missed, for example across a reconnect;
///     the client reads observations after its cursor over HTTP and drops sequences it already has.
/// </summary>
public sealed class BacktestHub(ISender sender, BacktestProgressSubscriptions subscriptions) : Hub
{
    public const string Path = "/ws/backtests";

    public async Task<BacktestCursorResponse> Subscribe(Guid runId)
    {
        Guid userId = (Context.User is { } user ? AuthenticationSetup.UserId(user) : null) ?? throw new HubException("Sign in to follow runs.");
        var query = new GetBacktestCursorQuery(userId, runId);

        try
        {
            // Checks ownership before the connection joins the run's group.
            await sender.Send(query, Context.ConnectionAborted);
        }
        catch (ResourceNotFoundException exception)
        {
            throw new HubException(exception.Code);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BacktestProgressSubscriptions.Group(runId), Context.ConnectionAborted);

        // Read after joining: anything committed later is sent to this connection, so the cursor leaves no gap.
        BacktestCursorInternal cursor = await sender.Send(query, Context.ConnectionAborted);

        if (cursor.Outcome is null)
        {
            subscriptions.Watch(runId, cursor.LastSequence);
        }

        return new BacktestCursorResponse(cursor.LastSequence, cursor.Outcome);
    }

    public Task Unsubscribe(Guid runId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, BacktestProgressSubscriptions.Group(runId), Context.ConnectionAborted);
    }
}