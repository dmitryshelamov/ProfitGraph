using System.Text.Json;

using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Instruments;

/// <summary>
///     Reads an instruments response before the SDK does and rejects any rule that decimal could not hold exactly, so a
///     captured snapshot never carries a silently rounded step, tick or minimum.
/// </summary>
internal sealed class BybitInstrumentsResponseGuardHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        try
        {
            if (response.IsSuccessStatusCode)
            {
                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                try
                {
                    using JsonDocument json = JsonDocument.Parse(body);
                    JsonElement root = json.RootElement;

                    if (root.GetProperty("retCode").GetInt32() == 0)
                    {
                        Validate(root.GetProperty("result"));
                    }
                }
                catch (Exception exception) when (exception is JsonException or FormatException or OverflowException or InvalidOperationException
                                                      or KeyNotFoundException)
                {
                    throw new BybitPayloadException("Bybit returned missing, malformed or inexact instrument rules.");
                }
            }

            return response;
        }
        catch
        {
            response.Dispose();

            throw;
        }
    }

    private static void Validate(JsonElement result)
    {
        JsonElement list = result.GetProperty("list");

        if (result.GetProperty("category").GetString() != "spot" || list.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException();
        }

        // An empty list is an unknown symbol and the status is checked by the source: request errors, not data errors.
        foreach (JsonElement row in list.EnumerateArray())
        {
            JsonElement lot = row.GetProperty("lotSizeFilter");
            BybitExactDecimal.Validate(lot.GetProperty("basePrecision"));
            BybitExactDecimal.Validate(lot.GetProperty("quotePrecision"));
            BybitExactDecimal.Validate(lot.GetProperty("minOrderAmt"));
            BybitExactDecimal.Validate(row.GetProperty("priceFilter").GetProperty("tickSize"));
        }
    }
}