using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.StopBotsForVenueAccount;

/// <summary>
///     A venue module's account protection opened: every bot of the credential that should run degrades with
///     <c>VenueAccountProtection</c> and sends nothing until the protection resumes (ADR 0022, C7). Orders stay as they are.
/// </summary>
public sealed record StopBotsForVenueAccountCommand(Guid CredentialId) : ICommand;