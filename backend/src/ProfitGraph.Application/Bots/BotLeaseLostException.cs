using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     The runtime no longer holds the bot's lease under its epoch, so it must stop executing the bot; nothing it tried to
///     write was committed.
/// </summary>
public sealed class BotLeaseLostException() : ApplicationExceptionBase(BotErrorCodes.LeaseLost, "The bot's lease is held by another runtime or expired.");