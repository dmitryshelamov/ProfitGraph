import { describe, expect, it } from 'vitest';

import { botStatusLabel, feedReasonLabel, reasonLabel } from './botStatus';
import type { Bot } from './types';

const failed: Pick<Bot, 'desiredState' | 'runtimeStatus' | 'statusReason'> = {
  desiredState: 'Running',
  runtimeStatus: 'Degraded',
  statusReason: 'RuntimeFailure',
};

describe('botStatus', () => {
  it('promises a restart after a runtime failure, however many restarts failed (ADR 0050)', () => {
    expect(botStatusLabel(failed)).toBe(
      'Остановлен по безопасности: сбой выполнения, бот перезапустится',
    );
  });

  it('says a safety reason waits for the owner', () => {
    expect(botStatusLabel({ ...failed, statusReason: 'HistoryDataCorrection' })).toBe(
      'Остановлен по безопасности: площадка исправила историю свечей, бот ждёт владельца',
    );
    expect(reasonLabel('HistoryDataCorrection')).toContain('ждёт владельца');
  });
});

describe('feedReasonLabel', () => {
  it('names why an order was rejected, and why the bot stopped for other entries', () => {
    expect(feedReasonLabel('OrderRejected', 'DuplicateRequest')).toContain('копии бота');
    expect(feedReasonLabel('OrderRejected', 'BrokerRejected')).toBe('биржа отклонила заявку');
    expect(feedReasonLabel('Degraded', 'HistoryDataCorrection')).toContain('ждёт владельца');
  });
});
