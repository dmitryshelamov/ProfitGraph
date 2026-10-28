using ProfitGraph.Application.Notifications.Contracts;

namespace ProfitGraph.Application.Notifications.Updates;

/// <summary>
///     Handles one command sent to the bot in a private chat (ADR 0042), such as <c>/start</c>.
/// </summary>
public interface ITelegramMessageHandler
{
    /// <summary>
    ///     The command in lower case, with its slash.
    /// </summary>
    string Command { get; }

    /// <summary>
    ///     Handles the message; <paramref name="arguments" /> are the words after the command.
    /// </summary>
    Task Handle(TelegramIncomingMessageInternal message, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}