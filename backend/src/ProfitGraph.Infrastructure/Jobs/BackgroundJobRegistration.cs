using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     One registered kind of work: its stable name, the request and result types stored as JSON and the Worker lane
///     that runs it.
/// </summary>
internal sealed record BackgroundJobRegistration(BackgroundJobKind Kind, Type RequestType, Type ResultType, string Lane);