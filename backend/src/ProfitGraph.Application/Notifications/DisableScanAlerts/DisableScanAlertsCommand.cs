using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Notifications.DisableScanAlerts;

/// <summary>
///     The «Отключить» button under a scanner alert (ADR 0059): switches off the alerts of the preset, only when it belongs to the user
///     of the linked chat. Answers whether alerts were on.
/// </summary>
public sealed record DisableScanAlertsCommand(long ChatId, Guid PresetId) : ICommand<bool>;