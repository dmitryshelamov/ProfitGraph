using MediatR;

using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Common.Transactions;

namespace ProfitGraph.Infrastructure.Common.Messaging;

/// <summary>
///     Runs every command in one database transaction. Queries and commands marked
///     <see cref="INonTransactionalCommand" /> run without one.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>(ITransactionExecutor transactionExecutor) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly bool IsQuery = typeof(TRequest)
        .GetInterfaces()
        .Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQuery<>));

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (IsQuery || request is INonTransactionalCommand)
        {
            return next(cancellationToken);
        }

        return transactionExecutor.ExecuteAsync(() => next(cancellationToken), cancellationToken);
    }
}