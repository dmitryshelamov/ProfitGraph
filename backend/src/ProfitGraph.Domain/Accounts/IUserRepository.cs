namespace ProfitGraph.Domain.Accounts;

public interface IUserRepository
{
    Task<User?> GetById(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByLogin(string login, CancellationToken cancellationToken);

    /// <summary>
    ///     Counts active Owners and locks their rows until the transaction ends, so concurrent blocks cannot remove the last one.
    /// </summary>
    Task<int> CountActiveOwnersForUpdate(CancellationToken cancellationToken);

    /// <summary>
    ///     Adds the account, or returns false when the login is already taken.
    /// </summary>
    Task<bool> TryAdd(User user, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds the account only when no account exists yet; returns whether it was added.
    /// </summary>
    Task<bool> TryAddFirst(User user, CancellationToken cancellationToken);

    Task Update(User user, CancellationToken cancellationToken);
}