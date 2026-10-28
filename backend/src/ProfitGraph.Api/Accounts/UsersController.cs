using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Accounts.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Accounts.BlockUser;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.CreateUser;

namespace ProfitGraph.Api.Accounts;

[ApiController]
[Authorize(Roles = AuthenticationSetup.OwnerRole)]
[Route("api/users")]
public sealed class UsersController(ISender sender, AccountConnections connections) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        UserInternal user = await sender.Send(new CreateUserCommand(request.Login ?? string.Empty, request.Password ?? string.Empty, request.Role), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, user.ToResponse());
    }

    [HttpPost("{id:guid}/block")]
    public async Task<IActionResult> Block(Guid id, CancellationToken cancellationToken)
    {
        Guid actorId = AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
        await sender.Send(new BlockUserCommand(actorId, id), cancellationToken);

        // HTTP requests re-check the account on their own; open realtime connections would outlive the block.
        connections.Abort(id);

        return NoContent();
    }
}