using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.BlockUser;
using ProfitGraph.Application.Accounts.CreateUser;

namespace ProfitGraph.Api.Accounts;

[ApiController]
[Authorize(Roles = AuthenticationSetup.OwnerRole)]
[Route("api/users")]
public sealed class UsersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        UserContract user = await sender.Send(new CreateUserCommand(request.Login ?? string.Empty, request.Password ?? string.Empty, request.Role), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, UserResponse.From(user));
    }

    [HttpPost("{id:guid}/block")]
    public async Task<IActionResult> Block(Guid id, CancellationToken cancellationToken)
    {
        Guid actorId = AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
        await sender.Send(new BlockUserCommand(actorId, id), cancellationToken);

        return NoContent();
    }
}