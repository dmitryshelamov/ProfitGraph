import type { StrategyDefinition } from '@/modules/strategies';

import { connectBlocks } from '../model/editorCommands';

export interface Connection {
  readonly source: string;
  readonly target: string;
  readonly sourceHandle?: string | null;
  readonly targetHandle?: string | null;
}
export function isValidConnection(connection: Connection, definition: StrategyDefinition): boolean {
  if (!connection.sourceHandle || !connection.targetHandle) {
    return false;
  }
  try {
    connectBlocks(
      { nodeId: connection.source, port: connection.sourceHandle },
      connection.target,
    ).apply({ name: '', definition, layout: { positions: {} } });
    return true;
  } catch {
    return false;
  }
}
