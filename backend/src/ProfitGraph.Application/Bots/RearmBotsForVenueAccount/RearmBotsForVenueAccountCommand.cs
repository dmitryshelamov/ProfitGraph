using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.RearmBotsForVenueAccount;

/// <summary>
///     A venue account's protection resumed: the bots it stopped return to Created, so the runtime takes them again and
///     reconciles before sending (ADR 0022, C7).
/// </summary>
public sealed record RearmBotsForVenueAccountCommand(Guid CredentialId) : ICommand;