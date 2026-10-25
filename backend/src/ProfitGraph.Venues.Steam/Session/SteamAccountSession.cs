using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Venues.Steam.Accounts;

namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     The one Community session of a Steam credential (Profit Fox Designer ADR 0083). It alone finalizes a sign-in with
///     the stored refresh token, transfers it to Community and keeps the cookies; concurrent callers share one renewal. An
///     unauthorized read renews once and is sent once more; a write is never sent twice. The server clock is measured at
///     the first use and again after <see cref="SteamOptions.ServerClockRefresh" />.
/// </summary>
internal sealed class SteamAccountSession(
    Guid ownerId,
    Guid credentialId,
    SteamHttpTransport transport,
    IServiceScopeFactory scopes,
    IOptions<SteamOptions> options,
    TimeProvider clock)
{
    private readonly SemaphoreSlim _renewal = new SemaphoreSlim(initialCount: 1, maxCount: 1);
    private readonly SemaphoreSlim _clockRefresh = new SemaphoreSlim(initialCount: 1, maxCount: 1);
    private SteamSessionSnapshot? _current;
    private SteamServerClock? _serverClock;
    private long _generation;

    public Guid OwnerId { get; } = ownerId;

    public async Task<SteamRequestResult> Execute(SteamHttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            SteamSessionSnapshot session = await Ensure(cancellationToken);
            SteamRequestResult result = await transport.Send(WithSessionId(request, session), session.Cookies, cancellationToken);

            if (result.Outcome != SteamRequestOutcome.Unauthorized)
            {
                return result;
            }

            Invalidate(session.Generation);

            if (!request.IsRead)
            {
                return result;
            }

            SteamSessionSnapshot renewed = await Ensure(cancellationToken);

            return await transport.Send(WithSessionId(request, renewed), renewed.Cookies, cancellationToken);
        }
        catch (SteamSessionException exception)
        {
            return new SteamRequestResult(exception.Outcome);
        }
    }

    /// <summary>
    ///     Steam's current time, for signing Guard codes and confirmations.
    /// </summary>
    public async Task<DateTimeOffset> ServerTime(CancellationToken cancellationToken)
    {
        SteamServerClock serverClock = await RefreshClock(cancellationToken);

        return clock.GetUtcNow() + serverClock.Offset;
    }

    private static SteamHttpRequest WithSessionId(SteamHttpRequest request, SteamSessionSnapshot session)
    {
        return request.Form is { } form && !request.IsRead
            ? request with { Form = new Dictionary<string, string>(form, StringComparer.Ordinal) { ["sessionid"] = session.SessionId } }
            : request;
    }

    private static string Text(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
    }

    private void Invalidate(long generation)
    {
        SteamSessionSnapshot? current = Volatile.Read(ref _current);

        if (current?.Generation == generation)
        {
            Interlocked.CompareExchange(ref _current, value: null, current);
        }
    }

    private async Task<SteamSessionSnapshot> Ensure(CancellationToken cancellationToken)
    {
        if (Usable(Volatile.Read(ref _current)) is { } current)
        {
            return current;
        }

        await _renewal.WaitAsync(cancellationToken);

        try
        {
            if (Usable(Volatile.Read(ref _current)) is { } renewedMeanwhile)
            {
                return renewedMeanwhile;
            }

            (string steamId, string refreshToken) = await ReadCredential(cancellationToken);
            SteamSessionSnapshot renewed = await Renew(steamId, refreshToken, cancellationToken);
            Volatile.Write(ref _current, renewed);

            return renewed;
        }
        finally
        {
            _renewal.Release();
        }
    }

    private SteamSessionSnapshot? Usable(SteamSessionSnapshot? session)
    {
        return session is not null && session.ExpiresAt > clock.GetUtcNow() + options.Value.SessionRenewalLead ? session : null;
    }

    private async Task<(string SteamId, string RefreshToken)> ReadCredential(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ExchangeCredentialSecretsInternal secrets = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReadExchangeCredentialSecretsQuery(OwnerId, credentialId), cancellationToken);

        if (secrets.Venue != SteamVenue.VenueId.Value)
        {
            throw new SteamSessionException(SteamRequestOutcome.NotSent, "The credential is not a Steam credential.");
        }

        return (secrets.Field(SteamCredentialFields.SteamId), secrets.Field(SteamCredentialFields.RefreshToken));
    }

    private async Task<SteamSessionSnapshot> Renew(string steamId, string refreshToken, CancellationToken cancellationToken)
    {
        string sessionId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        var finalize = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nonce"] = refreshToken,
            ["sessionid"] = sessionId,
            ["redir"] = new Uri(options.Value.Address(SteamEndpoint.Community), "/login/home/?goto=").AbsoluteUri
        };
        SteamHttpResponse login = Require(
            await transport.Send(
                new SteamHttpRequest(SteamEndpoint.Login, "/jwt/finalizelogin", finalize, IsRead: false, Multipart: true),
                cookies: null,
                cancellationToken));

        try
        {
            using JsonDocument document = JsonDocument.Parse(login.Body);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("steamID", out JsonElement returned) || Text(returned) != steamId)
            {
                throw new SteamSessionException(SteamRequestOutcome.InvalidResponse, "The Steam session belongs to another account.");
            }

            Uri community = options.Value.Address(SteamEndpoint.Community);
            JsonElement transfer = root.GetProperty("transfer_info")
                .EnumerateArray()
                .FirstOrDefault(item => item.TryGetProperty("url", out JsonElement url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? target) &&
                    target.Scheme == community.Scheme && target.Authority == community.Authority &&
                    target.AbsolutePath.StartsWith("/login/", StringComparison.Ordinal));

            if (transfer.ValueKind != JsonValueKind.Object)
            {
                throw new SteamSessionException(SteamRequestOutcome.InvalidResponse, "Steam returned no Community transfer.");
            }

            var destination = new Uri(transfer.GetProperty("url").GetString()!);
            Dictionary<string, string> values = transfer.GetProperty("params")
                .EnumerateObject()
                .ToDictionary(property => property.Name, property => Text(property.Value), StringComparer.Ordinal);
            values["steamID"] = steamId;
            SteamHttpResponse transferred = Require(
                await transport.Send(
                    new SteamHttpRequest(SteamEndpoint.Community, destination.PathAndQuery, values, IsRead: false, Multipart: true),
                    cookies: null,
                    cancellationToken));
            var cookies = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string header in transferred.SetCookies)
            {
                string[] pair = header.Split(';')[0].Split(separator: '=', count: 2);

                if (pair.Length == 2 && pair[0].Trim().Length > 0)
                {
                    cookies[pair[0].Trim()] = pair[1];
                }
            }

            if (!cookies.TryGetValue("steamLoginSecure", out string? secure) || !Uri.UnescapeDataString(secure).StartsWith(steamId + "||", StringComparison.Ordinal))
            {
                throw new SteamSessionException(SteamRequestOutcome.InvalidResponse, "Steam returned no Community sign-in of this account.");
            }

            cookies["sessionid"] = sessionId;

            return new SteamSessionSnapshot(
                Interlocked.Increment(ref _generation),
                sessionId,
                string.Join("; ", cookies.Select(pair => pair.Key + "=" + pair.Value)),
                Expiry(secure));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or UriFormatException)
        {
            throw new SteamSessionException(SteamRequestOutcome.InvalidResponse, "Steam returned incomplete session data.");
        }
    }

    /// <summary>
    ///     The session cookie carries a token whose <c>exp</c> bounds the session; without a readable one, and never later
    ///     than <see cref="SteamOptions.SessionLifetime" /> from now.
    /// </summary>
    private DateTimeOffset Expiry(string secureCookie)
    {
        DateTimeOffset fallback = clock.GetUtcNow() + options.Value.SessionLifetime;

        try
        {
            string token = Uri.UnescapeDataString(secureCookie).Split("||", count: 2)[1];
            string payload = token.Split('.')[1].Replace(oldChar: '-', newChar: '+').Replace(oldChar: '_', newChar: '/');
            using JsonDocument document = JsonDocument.Parse(
                Encoding.UTF8.GetString(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, paddingChar: '='))));
            DateTimeOffset expires = DateTimeOffset.FromUnixTimeSeconds(document.RootElement.GetProperty("exp").GetInt64());

            return expires < fallback ? expires : fallback;
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or FormatException or JsonException or KeyNotFoundException or InvalidOperationException
                                              or ArgumentOutOfRangeException)
        {
            return fallback;
        }
    }

    private async Task<SteamServerClock> RefreshClock(CancellationToken cancellationToken)
    {
        if (Fresh(Volatile.Read(ref _serverClock)) is { } current)
        {
            return current;
        }

        await _clockRefresh.WaitAsync(cancellationToken);

        try
        {
            if (Fresh(Volatile.Read(ref _serverClock)) is { } measuredMeanwhile)
            {
                return measuredMeanwhile;
            }

            DateTimeOffset started = clock.GetUtcNow();
            SteamHttpResponse response = Require(
                await transport.Send(
                    new SteamHttpRequest(SteamEndpoint.Api, "/ITwoFactorService/QueryTime/v1/", new Dictionary<string, string>(), IsRead: true),
                    cookies: null,
                    cancellationToken));

            try
            {
                using JsonDocument document = JsonDocument.Parse(response.Body);
                string seconds = Text(document.RootElement.GetProperty("response").GetProperty("server_time"));

                if (!long.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out long unix) || unix <= 0)
                {
                    throw new FormatException();
                }

                DateTimeOffset now = clock.GetUtcNow();
                var measured = new SteamServerClock(DateTimeOffset.FromUnixTimeSeconds(unix) - (started + (now - started) / 2), now);
                Volatile.Write(ref _serverClock, measured);

                return measured;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException
                                                  or ArgumentOutOfRangeException)
            {
                throw new SteamSessionException(SteamRequestOutcome.InvalidResponse, "Steam's server time is unreadable.");
            }
        }
        finally
        {
            _clockRefresh.Release();
        }
    }

    private SteamServerClock? Fresh(SteamServerClock? serverClock)
    {
        return serverClock is not null && clock.GetUtcNow() - serverClock.MeasuredAt < options.Value.ServerClockRefresh ? serverClock : null;
    }

    private static SteamHttpResponse Require(SteamRequestResult result)
    {
        return result.IsSuccess ? result.Response! : throw new SteamSessionException(result.Outcome, $"A Steam session request ended as {result.Outcome}.");
    }
}