import { isLiveTrade, settingsProblem, tradeFailure, tradeStatus } from './trades';
import type { ScanLiveTrade } from './types';

describe('trade cards', () => {
  it('asks for a key and positive exact amounts', () => {
    expect(settingsProblem('', '5', '100')).toBe('Добавьте ключ этой биржи в разделе «Ключи».');
    expect(settingsProblem('key', '0', '100')).toBe(
      'Риск — положительное число, например 5 или 2.5.',
    );
    expect(settingsProblem('key', '5', '1e3')).toBe(
      'Максимум — положительное число, например 5 или 2.5.',
    );
    expect(settingsProblem('key', ' 2.5 ', '100')).toBeNull();
  });

  it('names statuses and failures in Russian and keeps unknown codes', () => {
    expect(tradeStatus('StoppedOut')).toEqual({ label: 'Стоп', tone: 'danger' });
    expect(tradeStatus('Later')).toEqual({ label: 'Later', tone: 'neutral' });
    expect(tradeFailure('scan_trade_entry_ended')).toBe('биржа не приняла вход');
    expect(tradeFailure(null)).toBeNull();
  });

  it('treats a waiting or open trade as live', () => {
    const trade = { status: 'Open' } as ScanLiveTrade;

    expect(isLiveTrade(trade)).toBe(true);
    expect(isLiveTrade({ ...trade, status: 'TookProfit' })).toBe(false);
  });
});
