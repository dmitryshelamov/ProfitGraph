using MediatR;

namespace ProfitGraph.Domain.Common;

/// <summary>
///     Describes a business fact produced by a domain aggregate.
///     MediatR dispatches it to registered notification handlers.
/// </summary>
public interface IDomainEvent : INotification;