using MediatR;

using Microsoft.AspNetCore.SignalR;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Bots.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Api.Bots.Realtime;

/// <summary>
///     <c>/ws/forming-candles</c> (ADR 0026): a client follows its own bot's forming candle and receives <c>FormingCandle</c>
///     and <c>FormingStatus</c> messages. A connection follows one bot at a time; the feed is shared by every client of the same
///     series and closes with its last client, which also leaves when the connection drops. Another user's bot is refused as missing.
/// </summary>
public sealed class FormingCandleHub(ISender sender, IFormingCandleFeeds feeds) : Hub
{
    public const string Path = "/ws/forming-candles";

    private const string GroupItem = "forming-group";

    public async Task<FormingFeedSnapshotResponse> Subscribe(Guid botId)
    {
        Guid userId = (Context.User is { } user ? AuthenticationSetup.UserId(user) : null) ?? throw new HubException("Sign in to follow candles.");
        FormingFeedSnapshotInternal snapshot;

        try
        {
            snapshot = await sender.Send(new JoinBotFormingFeedCommand(userId, botId, Context.ConnectionId), Context.ConnectionAborted);
        }
        catch (ResourceNotFoundException exception)
        {
            throw new HubException(exception.Code);
        }

        await LeaveGroup();

        if (Context.ConnectionAborted.IsCancellationRequested)
        {
            await feeds.Leave(Context.ConnectionId);
        }
        else if (snapshot.Status != FormingFeedStatus.Unsupported)
        {
            string group = Group(snapshot.Key);
            Context.Items[GroupItem] = group;
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        }

        return snapshot.ToResponse();
    }

    public async Task Unsubscribe()
    {
        await feeds.Leave(Context.ConnectionId);
        await LeaveGroup();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await feeds.Leave(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    internal static string Group(FormingFeedKeyInternal key)
    {
        return $"forming:{key.Venue}:{key.Symbol}:{key.Timeframe}:{key.CredentialId:N}";
    }

    private async Task LeaveGroup()
    {
        if (Context.Items.Remove(GroupItem, out object? group) && group is string name)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, name);
        }
    }
}