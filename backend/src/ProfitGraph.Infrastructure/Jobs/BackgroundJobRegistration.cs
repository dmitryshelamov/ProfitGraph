using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     One registered kind of work: its stable name and the request and result types stored as JSON.
/// </summary>
internal sealed record BackgroundJobRegistration(BackgroundJobKind Kind, Type RequestType, Type ResultType);