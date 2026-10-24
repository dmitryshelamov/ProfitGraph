using System.Text.Json;

namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     Reads a kline response before the SDK does and rejects any price or volume that decimal could not hold exactly,
///     so the SDK never rounds market data silently (Profit Fox Designer ADR 0017).
/// </summary>
internal sealed class BybitResponseGuardHandler : DelegatingHandler
{
    private const int MaxRows = 1000;

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

                    if (json.RootElement.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Object &&
                        result.TryGetProperty("list", out JsonElement list))
                    {
                        Validate(list);
                    }
                }
                catch (Exception exception) when (exception is JsonException or FormatException or OverflowException or InvalidOperationException
                                                      or KeyNotFoundException or IndexOutOfRangeException)
                {
                    throw new BybitPayloadException("Bybit returned malformed or unrepresentable candle values.");
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

    private static void Validate(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > MaxRows)
        {
            throw new FormatException();
        }

        foreach (JsonElement row in list.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 6)
            {
                throw new FormatException();
            }

            for (var index = 1; index <= 5; index++)
            {
                BybitExactDecimal.Validate(row[index]);
            }
        }
    }
}