namespace ProfitGraph.Application.Jobs.Contracts;

/// <summary>
///     Why a job failed: the stable code of the error and a message for people.
/// </summary>
public sealed record BackgroundJobFailureInternal(string Code, string Message);