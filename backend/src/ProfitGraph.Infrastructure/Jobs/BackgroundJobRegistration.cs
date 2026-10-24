using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     One registered kind of work: its stable name, the request and result types stored as JSON, the Worker lane that
///     runs it and whether an interrupted job resumes instead of failing.
/// </summary>
internal sealed record BackgroundJobRegistration(BackgroundJobKind Kind, Type RequestType, Type ResultType, string Lane, bool Resumable = false);