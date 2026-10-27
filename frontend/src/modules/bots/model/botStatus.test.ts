import { describe, expect, it } from 'vitest';

import { botStatusLabel, reasonLabel, restartsExhausted } from './botStatus';
import type { Bot } from './types';

const failed: Pick<
  Bot,
  'desiredState' | 'runtimeStatus' | 'statusReason' | 'nextRestartAt' | 'restartAttempts'
> = {
  desiredState: 'Running',
  runtimeStatus: 'Degraded',
  statusReason: 'RuntimeFailure',
  nextRestartAt: '2026-10-27T01:00:30Z',
  restartAttempts: 1,
};

describe('botStatus', () => {
  it('promises a restart while one is scheduled', () => {
    expect(restartsExhausted(failed)).toBe(false);
    expect(botStatusLabel(failed)).toBe(
      'Остановлен по безопасности: сбой выполнения, бот перезапустится',
    );
  });

  it('says the bot waits for the owner once the automatic restarts are spent', () => {
    const spent = { ...failed, nextRestartAt: null, restartAttempts: 5 };

    expect(restartsExhausted(spent)).toBe(true);
    expect(botStatusLabel(spent)).toBe(
      'Остановлен по безопасности: сбой выполнения, бот ждёт владельца',
    );
  });

  it('does not mistake another reason without a restart time for spent restarts', () => {
    const killed = {
      ...failed,
      statusReason: 'HistoryDataCorrection' as const,
      nextRestartAt: null,
    };

    expect(restartsExhausted(killed)).toBe(false);
    expect(reasonLabel('HistoryDataCorrection')).toContain('ждёт владельца');
  });
});
