using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.CreateTelegramLinkCode;
using ProfitGraph.Application.Notifications.GetTelegramLink;
using ProfitGraph.Application.Notifications.UnlinkTelegram;
using ProfitGraph.Infrastructure.Notifications;

namespace ProfitGraph.Api.Notifications;

/// <summary>
///     The signed-in user's Telegram alerts (ADR 0020): its status, a one-time link code and unlinking.
/// </summary>
[ApiController]
[Route("api/profile/telegram")]
public sealed class TelegramController(ISender sender, IOptions<TelegramOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TelegramLinkResponse>> Get(CancellationToken cancellationToken)
    {
        TelegramLinkInternal link = await sender.Send(new GetTelegramLinkQuery(UserId()), cancellationToken);

        return new TelegramLinkResponse(options.Value.Enabled, link.Linked, link.LinkedAt, link.Blocked, options.Value.BotUsername);
    }

    [HttpPost("link-code")]
    public async Task<ActionResult<TelegramLinkCodeResponse>> CreateLinkCode(CancellationToken cancellationToken)
    {
        TelegramLinkCodeInternal code = await sender.Send(new CreateTelegramLinkCodeCommand(UserId()), cancellationToken);

        return new TelegramLinkCodeResponse(code.Code, code.ExpiresAt, options.Value.StartLink(code.Code));
    }

    [HttpDelete]
    public async Task<IActionResult> Unlink(CancellationToken cancellationToken)
    {
        await sender.Send(new UnlinkTelegramCommand(UserId()), cancellationToken);

        return NoContent();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}