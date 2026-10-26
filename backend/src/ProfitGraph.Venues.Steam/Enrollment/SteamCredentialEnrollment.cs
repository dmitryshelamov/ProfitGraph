using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam.Accounts;

namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     Links a new Steam Guard authenticator to an account from the web (ADR 0022; Profit Fox Designer's enrollment): a
///     password sign-in with the code Steam emails, <c>AddAuthenticator</c>, the recovery code shown once, then
///     <c>FinalizeAddAuthenticator</c> with the emailed activation code. The password is used for one request and the
///     recovery code appears only in that step's answer; neither is stored or logged. The pending state is encrypted like a
///     credential's secrets and expires <see cref="SteamOptions.EnrollmentLifetime" /> after its last step. Each step moves
///     the stored step first, so it runs once; a write whose answer is unclear stops the enrollment instead of being sent
///     again. The stored account waits in <see cref="SteamGuardStatus.CoolingDown" /> until its eligibility check.
/// </summary>
internal sealed class SteamCredentialEnrollment(
    ISteamEnrollmentStore store,
    SteamEnrollmentClient client,
    ISecretProtector protector,
    IServiceScopeFactory scopes,
    IOptions<SteamOptions> options,
    TimeProvider clock) : IVenueCredentialEnrollment
{
    public const string EmailCode = "email-code";
    public const string SignInPending = "sign-in-pending";
    public const string RecoverySaved = "recovery-saved";
    public const string Activate = "activate";

    private const string Adding = "adding";
    private const string Activating = "activating";
    private const string Activated = "activated";
    private const string StateField = "steamEnrollment";

    public VenueId Venue => SteamVenue.VenueId;

    public async Task<CredentialEnrollmentStepInternal> Start(
        Guid ownerId,
        Guid enrollmentId,
        string label,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        if (label.Trim().Length is 0 or > ExchangeCredentialLabel.MaxLength || Field(fields, "accountName", maximum: 64) is not { } accountName ||
            Field(fields, "password", maximum: 256) is not { } password)
        {
            return Stopped("invalid_fields");
        }

        SteamSignIn signIn;

        try
        {
            signIn = await client.Begin(enrollmentId, accountName, password, cancellationToken);
        }
        catch (SteamEnrollmentException exception)
        {
            return Stopped(exception.Problem);
        }

        // A code from an existing authenticator means the account has one: it is imported, not linked again.
        if (signIn.Confirmations.Contains(3))
        {
            return Stopped("authenticator_exists");
        }

        var state = new SteamEnrollmentState(label.Trim(), signIn.SteamId, signIn.ClientId, signIn.RequestId, "android:" + Guid.NewGuid(), AccountName: accountName);
        bool email = signIn.Confirmations.Contains(2);
        DateTimeOffset now = clock.GetUtcNow();
        await store.Add(
            new SteamPendingEnrollment(
                enrollmentId,
                ownerId,
                email ? EmailCode : SignInPending,
                Encrypt(enrollmentId, ownerId, state),
                now + options.Value.EnrollmentLifetime,
                now,
                now),
            cancellationToken);

        if (!email)
        {
            return await SignedIn(enrollmentId, state, cancellationToken);
        }

        try
        {
            await client.CheckDevice(enrollmentId, signIn, cancellationToken);
        }
        catch (SteamEnrollmentException)
        {
            // Profit Fox Designer did not depend on its answer either.
        }

        return new CredentialEnrollmentStepInternal(EmailCode, ["code"]);
    }

    public async Task<CredentialEnrollmentStepInternal> Continue(
        Guid ownerId,
        Guid enrollmentId,
        string step,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        if (await store.Get(enrollmentId, cancellationToken) is not { } pending || pending.OwnerId != ownerId)
        {
            return Stopped("enrollment_not_found");
        }

        if (pending.ExpiresAt <= clock.GetUtcNow())
        {
            await store.Delete(enrollmentId, cancellationToken);

            return Stopped("enrollment_expired");
        }

        SteamEnrollmentState state = Decrypt(pending);

        if (pending.Step == Activated)
        {
            return Completed(state);
        }

        if (pending.Step is Adding or Activating)
        {
            return Stopped("enrollment_unknown");
        }

        if (step != pending.Step)
        {
            return View(pending.Step, "unexpected_step");
        }

        try
        {
            switch (pending.Step)
            {
                case EmailCode:
                    return await SubmitCode(pending, state, Field(fields, "code", maximum: 32), cancellationToken);
                case SignInPending:
                    return await SignedIn(enrollmentId, state, cancellationToken);
                case RecoverySaved:
                    return await Move(pending, Activate, state, cancellationToken) ? View(Activate) : Stopped("enrollment_busy");
                case Activate:
                    return Field(fields, "code", maximum: 32) is { } code
                        ? await Finalize(pending, state, code, cancellationToken)
                        : View(Activate, "invalid_fields");
                default:
                    return Stopped("enrollment_unknown");
            }
        }
        catch (SteamEnrollmentException exception)
        {
            return Stopped(exception.Problem);
        }
    }

    public async Task Stored(Guid ownerId, Guid enrollmentId, CancellationToken cancellationToken)
    {
        if (await store.Get(enrollmentId, cancellationToken) is not { Step: Activated } pending || pending.OwnerId != ownerId)
        {
            return;
        }

        SteamEnrollmentState state = Decrypt(pending);
        IReadOnlyList<string> fingerprints = protector.Fingerprints(SteamCredentialFields.AccountFingerprint, state.SteamId);
        DateTimeOffset now = clock.GetUtcNow();
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISteamAccountStore>()
            .Add(new SteamAccount(enrollmentId, fingerprints[0], SteamGuardStatus.CoolingDown, now + options.Value.CoolingDownEstimate, now, now), cancellationToken);
        await store.Delete(enrollmentId, cancellationToken);
    }

    private async Task<CredentialEnrollmentStepInternal> SubmitCode(
        SteamPendingEnrollment pending,
        SteamEnrollmentState state,
        string? code,
        CancellationToken cancellationToken)
    {
        if (code is null)
        {
            return View(EmailCode, "invalid_fields");
        }

        var signIn = new SteamSignIn(state.ClientId, state.RequestId, state.SteamId, [2]);

        if (!await client.SubmitCode(pending.Id, signIn, code, cancellationToken))
        {
            return View(EmailCode, "invalid_code");
        }

        return await Move(pending, SignInPending, state, cancellationToken)
            ? await SignedIn(pending.Id, state, cancellationToken)
            : Stopped("enrollment_busy");
    }

    /// <summary>
    ///     Once Steam confirms the sign-in, refuses an account already stored, then adds the authenticator; its recovery code
    ///     is the only thing the answer carries.
    /// </summary>
    private async Task<CredentialEnrollmentStepInternal> SignedIn(Guid enrollmentId, SteamEnrollmentState state, CancellationToken cancellationToken)
    {
        if (await client.Poll(enrollmentId, new SteamSignIn(state.ClientId, state.RequestId, state.SteamId, []), cancellationToken) is not { } tokens)
        {
            return View(SignInPending);
        }

        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            if (await scope.ServiceProvider.GetRequiredService<ISteamAccountStore>()
                    .AnyFingerprint(protector.Fingerprints(SteamCredentialFields.AccountFingerprint, state.SteamId), cancellationToken))
            {
                await store.Delete(enrollmentId, cancellationToken);

                return Stopped("account_exists");
            }
        }

        SteamPendingEnrollment pending = await store.Get(enrollmentId, cancellationToken) ?? throw new SteamEnrollmentException("enrollment_not_found");
        SteamEnrollmentState signedIn = state with { RefreshToken = tokens.RefreshToken, AccessToken = tokens.AccessToken };

        if (!await Move(pending, Adding, signedIn, cancellationToken))
        {
            return Stopped("enrollment_busy");
        }

        SteamAddedAuthenticator added;

        try
        {
            added = await client.AddAuthenticator(enrollmentId, tokens.AccessToken, state.SteamId, state.DeviceId, cancellationToken);
        }
        catch (SteamEnrollmentException exception) when (exception.Problem is "phone_required" or "authenticator_exists" or "enrollment_refused")
        {
            // Steam refused for certain, so nothing was added and nothing waits.
            await store.Delete(enrollmentId, cancellationToken);

            return Stopped(exception.Problem);
        }

        if (added.ConfirmationType != 3)
        {
            return Stopped("activation_unsupported");
        }

        SteamPendingEnrollment adding = await store.Get(enrollmentId, cancellationToken) ?? throw new SteamEnrollmentException("enrollment_not_found");
        SteamEnrollmentState withSecrets = signedIn with { SharedSecret = added.SharedSecret, IdentitySecret = added.IdentitySecret };

        return await Move(adding, RecoverySaved, withSecrets, cancellationToken)
            ? new CredentialEnrollmentStepInternal(RecoverySaved, [], added.RecoveryCode)
            : Stopped("enrollment_unknown");
    }

    private async Task<CredentialEnrollmentStepInternal> Finalize(
        SteamPendingEnrollment pending,
        SteamEnrollmentState state,
        string code,
        CancellationToken cancellationToken)
    {
        if (!await Move(pending, Activating, state, cancellationToken))
        {
            return Stopped("enrollment_busy");
        }

        SteamPendingEnrollment activating = await store.Get(pending.Id, cancellationToken) ?? throw new SteamEnrollmentException("enrollment_not_found");

        for (var attempt = 0; attempt < options.Value.ActivationAttempts; attempt++)
        {
            DateTimeOffset serverTime = await client.ServerTime(pending.Id, cancellationToken);
            bool done;

            try
            {
                done = await client.Finalize(
                    pending.Id,
                    state.AccessToken!,
                    state.SteamId,
                    SteamGuardCode.For(state.SharedSecret!, serverTime),
                    serverTime,
                    code,
                    cancellationToken);
            }
            catch (SteamEnrollmentException exception) when (exception.Problem == "invalid_code")
            {
                // Steam says the code is wrong: nothing was activated, and the owner types it again.
                await Move(activating, Activate, state, cancellationToken);

                return View(Activate, "invalid_code");
            }

            if (done)
            {
                return await Move(activating, Activated, state with { AccessToken = null }, cancellationToken) ? Completed(state) : Stopped("enrollment_unknown");
            }
        }

        await Move(activating, Activate, state, cancellationToken);

        return View(Activate, "activation_pending");
    }

    private async Task<bool> Move(SteamPendingEnrollment pending, string step, SteamEnrollmentState state, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        return await store.Transition(
            pending with { Step = step, Payload = Encrypt(pending.Id, pending.OwnerId, state), ExpiresAt = now + options.Value.EnrollmentLifetime, UpdatedAt = now },
            pending.Step,
            cancellationToken);
    }

    private static CredentialEnrollmentStepInternal Completed(SteamEnrollmentState state)
    {
        return new CredentialEnrollmentStepInternal(
            CredentialEnrollmentStepInternal.CompletedStep,
            [],
            Completed: new EnrolledCredentialInternal(
                state.Label,
                state.SteamId[^4..],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [SteamCredentialFields.SteamId] = state.SteamId,
                    [SteamCredentialFields.RefreshToken] = state.RefreshToken!,
                    [SteamCredentialFields.SharedSecret] = state.SharedSecret!,
                    [SteamCredentialFields.IdentitySecret] = state.IdentitySecret!,
                    [SteamCredentialFields.DeviceId] = state.DeviceId
                },
                state.AccountName ?? state.SteamId));
    }

    private static CredentialEnrollmentStepInternal View(string step, string? problem = null)
    {
        IReadOnlyList<string> fields = step is EmailCode or Activate ? ["code"] : [];

        return new CredentialEnrollmentStepInternal(step, fields, Problem: problem);
    }

    private static CredentialEnrollmentStepInternal Stopped(string problem)
    {
        return new CredentialEnrollmentStepInternal(CredentialEnrollmentStepInternal.StoppedStep, [], Problem: problem);
    }

    private static string? Field(IReadOnlyDictionary<string, string> fields, string name, int maximum)
    {
        return fields.TryGetValue(name, out string? value) && value.Trim() is { Length: > 0 } trimmed && trimmed.Length <= maximum && !trimmed.Any(char.IsControl)
            ? name == "password" ? value : trimmed
            : null;
    }

    private EncryptedSecret Encrypt(Guid enrollmentId, Guid ownerId, SteamEnrollmentState state)
    {
        return protector.Encrypt(new ExchangeCredentialId(enrollmentId), new UserId(ownerId), StateField, JsonSerializer.Serialize(state));
    }

    private SteamEnrollmentState Decrypt(SteamPendingEnrollment pending)
    {
        return JsonSerializer.Deserialize<SteamEnrollmentState>(
                protector.Decrypt(new ExchangeCredentialId(pending.Id), new UserId(pending.OwnerId), StateField, pending.Payload)) ??
            throw new SteamEnrollmentException("enrollment_unknown");
    }
}