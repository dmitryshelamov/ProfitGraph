namespace ProfitGraph.Venues.Steam.Session;

internal sealed record SteamRequestResult(SteamRequestOutcome Outcome, SteamHttpResponse? Response = null, TimeSpan? RetryAfter = null)
{
    public bool IsSuccess => Outcome == SteamRequestOutcome.Success && Response is not null;
}