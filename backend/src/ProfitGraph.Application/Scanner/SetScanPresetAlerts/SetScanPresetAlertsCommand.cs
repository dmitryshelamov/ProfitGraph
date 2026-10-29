using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.SetScanPresetAlerts;

/// <summary>
///     Switches the Telegram alerts of the user's own preset on or off (ADR 0059). Switching them on reports only pairs that enter
///     the result after the next snapshot; switching them off forgets what the preset remembered.
/// </summary>
public sealed record SetScanPresetAlertsCommand(Guid UserId, Guid PresetId, bool Enabled) : ICommand<ScanPresetInternal>;