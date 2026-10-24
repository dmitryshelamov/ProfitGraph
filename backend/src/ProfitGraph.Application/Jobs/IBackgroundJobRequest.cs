using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs;

/// <summary>
///     Parameters of work that the Worker runs as a MediatR request (ADR 0011). The kind is the stable name stored with
///     the job; Infrastructure maps it back to the request type.
/// </summary>
public interface IBackgroundJobRequest
{
    static abstract BackgroundJobKind Kind { get; }
}