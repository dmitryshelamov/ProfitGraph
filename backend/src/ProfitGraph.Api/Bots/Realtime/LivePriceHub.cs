using MediatR;

using Microsoft.AspNetCore.SignalR;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBot;
using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Api.Bots.Realtime;

/// <summary>
///     <c>/ws/live-prices</c> (S5-19): a client subscribes to its own bots and receives <c>LivePrice</c> each time the
///     Worker stores a newer display price of the bot's instrument. Another user's bot is refused as missing.
/// </summary>
public sealed class LivePriceHub(ISender sender) : Hub
{
    public const string Path = "/ws/live-prices";

    public async Task Subscribe(Guid botId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(await Bot(botId)), Context.ConnectionAborted);
    }

    public async Task Unsubscribe(Guid botId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(await Bot(botId)), Context.ConnectionAborted);
    }

    internal static string Group(string venue, string symbol)
    {
        return $"live-price:{venue}:{symbol}";
    }

    private static string Group(BotInternal bot)
    {
        return Group(bot.Venue, bot.Symbol);
    }

    private async Task<BotInternal> Bot(Guid botId)
    {
        Guid userId = (Context.User is { } user ? AuthenticationSetup.UserId(user) : null) ?? throw new HubException("Sign in to follow prices.");

        try
        {
            return await sender.Send(new GetBotQuery(userId, botId), Context.ConnectionAborted);
        }
        catch (ResourceNotFoundException exception)
        {
            throw new HubException(exception.Code);
        }
    }
}