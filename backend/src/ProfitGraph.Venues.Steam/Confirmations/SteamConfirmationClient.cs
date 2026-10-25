using System.Globalization;
using System.Text.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Confirmations;

/// <summary>
///     Lists and approves an account's mobile confirmations (Profit Fox Designer <c>SteamMarketClient.Confirmations</c> and
///     <c>Approve</c>): each request is signed with the identity secret for Steam's time, read from the credential for that
///     request only. One confirmation is approved at a time; Steam's batch operation is not used.
/// </summary>
internal sealed class SteamConfirmationClient(IServiceScopeFactory scopes)
{
    /// <summary>
    ///     The pending confirmations, or null when Steam's answer cannot be trusted.
    /// </summary>
    public async Task<IReadOnlyList<SteamConfirmation>?> List(SteamAccountSession session, Guid credentialId, CancellationToken cancellationToken)
    {
        string query = await Signed(session, credentialId, SteamConfirmationKey.ListTag, [], cancellationToken);
        SteamRequestResult result = await session.Execute(SteamHttpRequest.Get(SteamEndpoint.Community, "/mobileconf/getlist?" + query), cancellationToken);

        return result.IsSuccess ? SteamConfirmationParser.Read(result.Response!.Body) : null;
    }

    /// <summary>
    ///     Approves one confirmation. Only Steam's <c>success</c> proves it; anything else may or may not have approved it,
    ///     and the next list tells.
    /// </summary>
    public async Task<SteamWriteReceiptKind> Approve(
        SteamAccountSession session,
        Guid credentialId,
        SteamConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        string query = await Signed(
            session,
            credentialId,
            SteamConfirmationKey.AcceptTag,
            [("op", "allow"), ("cid", confirmation.Id), ("ck", confirmation.Nonce)],
            cancellationToken);
        SteamRequestResult result = await session.Execute(
            new SteamHttpRequest(SteamEndpoint.Community, "/mobileconf/ajaxop?" + query, IsRead: false, Class: SteamRequestClass.Write),
            cancellationToken);

        if (result.Outcome is SteamRequestOutcome.NotSent or SteamRequestOutcome.Deferred)
        {
            return SteamWriteReceiptKind.NotSent;
        }

        try
        {
            return result.IsSuccess && JsonDocument.Parse(result.Response!.Body) is var document && Approved(document)
                ? SteamWriteReceiptKind.Accepted
                : SteamWriteReceiptKind.Unknown;
        }
        catch (JsonException)
        {
            return SteamWriteReceiptKind.Unknown;
        }
    }

    private static bool Approved(JsonDocument document)
    {
        using (document)
        {
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("success", out JsonElement success) &&
                success.ValueKind == JsonValueKind.True;
        }
    }

    private async Task<string> Signed(
        SteamAccountSession session,
        Guid credentialId,
        string tag,
        IReadOnlyList<(string Name, string Value)> extra,
        CancellationToken cancellationToken)
    {
        DateTimeOffset serverTime = await session.ServerTime(cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ExchangeCredentialSecretsInternal secrets = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReadExchangeCredentialSecretsQuery(session.OwnerId, credentialId), cancellationToken);

        if (secrets.Venue != SteamVenue.VenueId.Value)
        {
            throw new SteamOrderUnresolvedException("The credential is not a Steam credential.");
        }

        (string Name, string Value)[] fields =
        [
            ("p", secrets.Field(SteamCredentialFields.DeviceId)),
            ("a", secrets.Field(SteamCredentialFields.SteamId)),
            ("k", SteamConfirmationKey.Sign(secrets.Field(SteamCredentialFields.IdentitySecret), serverTime, tag)),
            ("t", serverTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ("m", "android"),
            ("tag", tag),
            .. extra
        ];

        return string.Join(separator: '&', fields.Select(field => Uri.EscapeDataString(field.Name) + "=" + Uri.EscapeDataString(field.Value)));
    }
}