namespace ProfitGraph.Application.Jobs;

/// <summary>
///     The result of a completed job. It is stored as JSON and shown to the user, so it carries no money: exact amounts
///     belong to the feature's own endpoints.
/// </summary>
public interface IBackgroundJobResult;