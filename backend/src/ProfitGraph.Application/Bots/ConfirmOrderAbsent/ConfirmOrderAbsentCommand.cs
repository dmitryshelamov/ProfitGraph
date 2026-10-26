using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.ConfirmOrderAbsent;

/// <summary>
///     The owner confirms that a submit of their bot whose delivery is unknown never reached the venue (Profit Fox Designer
///     ADR 0055). The bot's next reconciliation looks the order up first and rejects it only when the venue shows nothing.
/// </summary>
public sealed record ConfirmOrderAbsentCommand(Guid OwnerId, Guid BotId, Guid IntentId) : ICommand;