using MediatR;

namespace ProfitGraph.Application.Common.Messaging;

public interface ICommand : IRequest;

public interface ICommand<out TResponse> : IRequest<TResponse>;