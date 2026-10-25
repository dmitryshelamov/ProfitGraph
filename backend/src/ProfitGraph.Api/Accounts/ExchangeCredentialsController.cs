using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Accounts.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.CreateExchangeCredential;
using ProfitGraph.Application.Accounts.DeleteExchangeCredential;
using ProfitGraph.Application.Accounts.GetAccountHistory;
using ProfitGraph.Application.Accounts.GetExchangeCredentials;

namespace ProfitGraph.Api.Accounts;

/// <summary>
///     The signed-in user's exchange credentials (ADR 0015). No response carries a secret; another user's credential
///     answers 404, as if it did not exist.
/// </summary>
[ApiController]
[Route("api/exchange-credentials")]
public sealed class ExchangeCredentialsController(ISender sender, TimeProvider clock) : ControllerBase
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

    /// <summary>
    ///     The account value history of the credential's exchange account (Profit Fox Designer ADR 0069), by default the
    ///     last seven days. It includes deposits, withdrawals and price moves; it is not bot profit.
    /// </summary>
    [HttpGet("{id:guid}/account")]
    public async Task<ActionResult<AccountHistoryResponse>> Account(
        Guid id,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 2016,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset end = to ?? clock.GetUtcNow();
        var query = new GetAccountHistoryQuery(UserId(), id, from ?? end.AddDays(-7), end, limit);

        return (await sender.Send(query, cancellationToken)).ToResponse();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}