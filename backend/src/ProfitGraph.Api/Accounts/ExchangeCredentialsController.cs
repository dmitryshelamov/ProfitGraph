using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Accounts.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.CreateExchangeCredential;
using ProfitGraph.Application.Accounts.DeleteExchangeCredential;
using ProfitGraph.Application.Accounts.EnrollCredential;
using ProfitGraph.Application.Accounts.GetAccountHistory;
using ProfitGraph.Application.Accounts.GetAccountProtection;
using ProfitGraph.Application.Accounts.GetAccountStatus;
using ProfitGraph.Application.Accounts.GetAuthenticatorCode;
using ProfitGraph.Application.Accounts.GetExchangeCredentials;
using ProfitGraph.Application.Accounts.ListVenueAccounts;
using ProfitGraph.Application.Accounts.RenameExchangeCredential;
using ProfitGraph.Application.Accounts.ResumeAccountProtection;

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
            new CreateExchangeCredentialCommand(UserId(), request.Venue, request.Label, request.ApiKey, request.ApiSecret, request.Fields),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, credential.ToResponse());
    }

    /// <summary>
    ///     The accounts a credential not saved yet can select (ADR 0023, TC5); the fields are used for this request only.
    /// </summary>
    [HttpPost("accounts")]
    public async Task<ActionResult<IReadOnlyList<VenueAccountResponse>>> Accounts(ListVenueAccountsRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<VenueAccountInternal> accounts = await sender.Send(new ListVenueAccountsQuery(UserId(), request.Venue, request.Fields), cancellationToken);

        return accounts.Select(account => account.ToResponse()).ToArray();
    }

    [HttpPut("{id:guid}/label")]
    public async Task<ActionResult<ExchangeCredentialResponse>> Rename(Guid id, RenameExchangeCredentialRequest request, CancellationToken cancellationToken)
    {
        return (await sender.Send(new RenameExchangeCredentialCommand(UserId(), id, request.Label), cancellationToken)).ToResponse();
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

    [HttpGet("{id:guid}/protection")]
    public async Task<ActionResult<AccountProtectionResponse>> Protection(Guid id, CancellationToken cancellationToken)
    {
        VenueAccountProtectionInternal protection = await sender.Send(new GetAccountProtectionQuery(UserId(), id), cancellationToken);

        return new AccountProtectionResponse(protection.Open, protection.Reason, protection.Since, protection.AutomaticResumeAt);
    }

    [HttpGet("{id:guid}/venue-status")]
    public async Task<ActionResult<AccountStatusResponse>> VenueStatus(Guid id, CancellationToken cancellationToken)
    {
        VenueAccountStatusInternal status = await sender.Send(new GetAccountStatusQuery(UserId(), id), cancellationToken);

        return new AccountStatusResponse(status.Status, status.NextCheckAt);
    }

    [HttpPost("{id:guid}/protection/resume")]
    public async Task<IActionResult> ResumeProtection(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ResumeAccountProtectionCommand(UserId(), id), cancellationToken);

        return NoContent();
    }

    /// <summary>
    ///     The current code of the credential's authenticator, for the Owner's own credential only; the response is never
    ///     cached.
    /// </summary>
    [HttpGet("{id:guid}/authenticator-code")]
    [Authorize(Roles = AuthenticationSetup.OwnerRole)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AuthenticatorCodeResponse>> AuthenticatorCode(Guid id, CancellationToken cancellationToken)
    {
        AuthenticatorCodeInternal code = await sender.Send(new GetAuthenticatorCodeQuery(UserId(), id), cancellationToken);

        return new AuthenticatorCodeResponse(code.Code, code.ValidUntil);
    }

    /// <summary>
    ///     Starts creating a credential with a venue that enrolls them in steps, such as linking a new authenticator; the answer
    ///     names the next step and is never cached.
    /// </summary>
    [HttpPost("enrollments")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<CredentialEnrollmentResponse>> StartEnrollment(StartCredentialEnrollmentRequest request, CancellationToken cancellationToken)
    {
        CredentialEnrollmentInternal enrollment = await sender.Send(
            new StartCredentialEnrollmentCommand(
                UserId(),
                request.Venue ?? string.Empty,
                request.Label ?? string.Empty,
                request.Fields ?? new Dictionary<string, string>()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, EnrollmentResponse(enrollment));
    }

    /// <summary>
    ///     Answers the step an enrollment waits for; the answer names the next step and is never cached.
    /// </summary>
    [HttpPost("enrollments/{id:guid}/steps")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<CredentialEnrollmentResponse>> ContinueEnrollment(
        Guid id,
        ContinueCredentialEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        CredentialEnrollmentInternal enrollment = await sender.Send(
            new ContinueCredentialEnrollmentCommand(
                UserId(),
                request.Venue ?? string.Empty,
                id,
                request.Step ?? string.Empty,
                request.Fields ?? new Dictionary<string, string>()),
            cancellationToken);

        return EnrollmentResponse(enrollment);
    }

    private static CredentialEnrollmentResponse EnrollmentResponse(CredentialEnrollmentInternal enrollment)
    {
        return new CredentialEnrollmentResponse(
            enrollment.EnrollmentId,
            enrollment.Venue,
            enrollment.Step,
            enrollment.Fields,
            enrollment.OneTimeSecret,
            enrollment.Problem,
            enrollment.CredentialId);
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}