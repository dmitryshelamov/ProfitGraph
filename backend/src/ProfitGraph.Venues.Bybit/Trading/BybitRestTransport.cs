using System.Net;
using System.Text;
using System.Text.Json;

using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Sends one Bybit V5 request, signed when it carries a credential (ADR 0017). Failures carry no request text; HTTP
///     errors, rate limits, timeouts and unexpected bodies are <see cref="BybitRestOutcome.Unknown" />.
/// </summary>
internal static class BybitRestTransport
{
    public static async Task<BybitRestResult> Signed(
        HttpClient http,
        BybitServerClock serverClock,
        HttpMethod method,
        string pathAndQuery,
        string payload,
        string? body,
        ExchangeCredentialSecretsInternal credential,
        Func<CancellationToken, Task>? beforeSend,
        CancellationToken cancellationToken)
    {
        long? timestamp = await serverClock.Now(token => Public(http, "/v5/market/time", token), cancellationToken);

        if (timestamp is null)
        {
            return new BybitRestResult(BybitRestOutcome.Unknown);
        }

        var request = new HttpRequestMessage(method, pathAndQuery);
        BybitSigner.Sign(request, credential.ApiKey, credential.ApiSecret, timestamp.Value, payload);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        BybitRestResult result = await Send(http, request, beforeSend, cancellationToken);

        if (result.Code == 10002)
        {
            serverClock.Invalidate();
        }

        return result;
    }

    public static Task<BybitRestResult> Public(HttpClient http, string pathAndQuery, CancellationToken cancellationToken)
    {
        return Send(http, new HttpRequestMessage(HttpMethod.Get, pathAndQuery), beforeSend: null, cancellationToken);
    }

    private static async Task<BybitRestResult> Send(HttpClient http, HttpRequestMessage request, Func<CancellationToken, Task>? beforeSend, CancellationToken cancellationToken)
    {
        using (request)
        {
            if (beforeSend is not null)
            {
                await beforeSend(cancellationToken);
            }

            try
            {
                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests || !response.IsSuccessStatusCode)
                {
                    return new BybitRestResult(BybitRestOutcome.Unknown, (int)response.StatusCode);
                }

                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                JsonElement root = document.RootElement;
                int code = root.GetProperty("retCode").GetInt32();

                return new BybitRestResult(Outcome(code), code, root.Clone());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException
                                                  or KeyNotFoundException)
            {
                return new BybitRestResult(BybitRestOutcome.Unknown);
            }
        }
    }

    private static BybitRestOutcome Outcome(int code)
    {
        return code switch
        {
            0 => BybitRestOutcome.Accepted,
            110072 => BybitRestOutcome.Duplicate,
            110001 => BybitRestOutcome.NotFound,
            10001 or 110003 or 110004 or 110007 or 110012 or 110017 or 30208 or 30209 => BybitRestOutcome.Rejected,
            _ => BybitRestOutcome.Unknown
        };
    }
}
