namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     Whether the venue holds the account's bots back, the reason by its code, since when and when it resumes by itself.
/// </summary>
public sealed record AccountProtectionResponse(bool Open, string? Reason, DateTimeOffset? Since, DateTimeOffset? AutomaticResumeAt);