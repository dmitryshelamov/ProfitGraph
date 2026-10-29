using Microsoft.AspNetCore.SignalR;

namespace ProfitGraph.Api.Scanner.Realtime;

/// <summary>
///     <c>/ws/scanner</c> (ADR 0059): every signed-in client receives <c>ScanSnapshotChanged</c> when the scanner stores a new
///     snapshot and reads it again over HTTP. The snapshot is shared, so there is nothing to subscribe to.
/// </summary>
public sealed class ScannerHub : Hub
{
    public const string Path = "/ws/scanner";
    public const string SnapshotChanged = "ScanSnapshotChanged";
}