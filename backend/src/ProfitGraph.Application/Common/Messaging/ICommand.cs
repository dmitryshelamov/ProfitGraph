using MediatR;

namespace ProfitGraph.Application.Common.Messaging;

/// <summary>
///     A state change without a result.
/// </summary>
public interface ICommand : IRequest;