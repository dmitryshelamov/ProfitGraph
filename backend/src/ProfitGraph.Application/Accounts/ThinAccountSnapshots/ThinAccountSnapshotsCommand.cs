using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.ThinAccountSnapshots;

/// <summary>
///     Keeps one snapshot per credential and UTC hour, the latest, among those older than seven days; returns how many it
///     removed. Hourly history never expires.
/// </summary>
public sealed record ThinAccountSnapshotsCommand : ICommand<int>;