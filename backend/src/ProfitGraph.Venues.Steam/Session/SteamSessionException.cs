namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     A session could not be opened or renewed. The message names the cause, never a token, cookie or Steam ID.
/// </summary>
internal sealed class SteamSessionException(SteamRequestOutcome outcome, string message) : Exception(message)
{
    public SteamRequestOutcome Outcome { get; } = outcome;
}