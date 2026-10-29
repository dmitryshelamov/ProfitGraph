import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

/**
 * Follows /ws/scanner: `onChanged` runs whenever the scanner stores a new snapshot and after every reconnection, so the page
 * reads the snapshot again over HTTP. Returns the function that stops following.
 */
export function followScanSnapshots(onChanged: () => void): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl('/ws/scanner')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build();
  let stopped = false;
  const changed = () => {
    if (!stopped) {
      onChanged();
    }
  };

  connection.on('ScanSnapshotChanged', changed);
  connection.onreconnected(changed);
  void connection.start().catch(() => undefined);

  return () => {
    stopped = true;
    void connection.stop();
  };
}
