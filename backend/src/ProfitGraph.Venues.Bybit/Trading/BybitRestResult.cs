using System.Text.Json;

namespace ProfitGraph.Venues.Bybit.Trading;

internal sealed record BybitRestResult(BybitRestOutcome Outcome, int? Code = null, JsonElement Body = default);
