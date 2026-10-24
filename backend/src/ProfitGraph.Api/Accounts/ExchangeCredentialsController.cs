using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Accounts.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.CreateExchangeCredential;
using ProfitGraph.Application.Accounts.DeleteExchangeCredential;
using ProfitGraph.Application.Accounts.GetExchangeCredentials;

namespace ProfitGraph.Api.Accounts;

/// <summary>
///     The signed-in user's exchange credentials (ADR 0015). No response carries a secret; another user's credential
///     answers 404, as if it did not exist.
/// </summary>
[ApiController]
[Route("api/exchange-credentials")]
public sealed class ExchangeCredentialsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExchangeCredentialResponse>>> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<ExchangeCredentialInternal> credentials = await sender.Send(new GetExchangeCredentialsQuery(UserId()), cancellationToken);

        return credentials.Select(credential => credential.ToResponse()).ToArray();
    }

    [HttpPost]
    public async Task<ActionResult<ExchangeCredentialResponse>> Create(CreateExchangeCredentialRequest request, CancellationToken cancellationToken)
    {
        ExchangeCredentialInternal credential = await sender.Send(
            new CreateExchangeCredentialCommand(UserId(), request.Venue, request.Label, request.ApiKey, request.ApiSecret),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, credential.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteExchangeCredentialCommand(UserId(), id), cancellationToken);

        return NoContent();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}