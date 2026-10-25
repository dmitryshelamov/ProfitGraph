namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     Whether a venue account's protection holds the account's bots back (ADR 0022, C7): the reason by its code, since
///     when, and when it resumes by itself, if it will.
/// </summary>
public sealed record VenueAccountProtectionInternal(bool Open, string? Reason, DateTimeOffset? Since, DateTimeOffset? AutomaticResumeAt)
{
    public static readonly VenueAccountProtectionInternal Clear = new VenueAccountProtectionInternal(Open: false, Reason: null, Since: null, AutomaticResumeAt: null);
}