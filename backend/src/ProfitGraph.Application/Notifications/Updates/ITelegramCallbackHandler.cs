using ProfitGraph.Application.Notifications.Contracts;

namespace ProfitGraph.Application.Notifications.Updates;

/// <summary>
///     Handles presses of the bot's buttons whose data starts with <see cref="Action" /> and a colon (ADR 0042).
/// </summary>
public interface ITelegramCallbackHandler
{
    string Action { get; }

    /// <summary>
    ///     Handles a press from a private chat; <paramref name="argument" /> is the data after the colon. Returns the short
    ///     notice the press is answered with, or null.
    /// </summary>
    Task<string?> Handle(TelegramCallbackInternal callback, long chatId, string argument, CancellationToken cancellationToken);
}