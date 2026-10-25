using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.DispatchOrderIntent;

/// <summary>
///     Delivers the next due order intent of the bots that <see cref="HolderId" /> runs (ADR 0016); returns false when none
///     was due. Each step is its own short transaction, and no transaction spans the venue call.
/// </summary>
public sealed record DispatchOrderIntentCommand(Guid HolderId) : ICommand<bool>, INonTransactionalCommand;