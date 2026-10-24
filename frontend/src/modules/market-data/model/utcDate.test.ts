import { utcDayStart, utcToday } from './utcDate';

describe('UTC days', () => {
  it('turns a day into its UTC midnight', () => {
    expect(utcDayStart('2026-01-31')).toBe('2026-01-31T00:00:00.000Z');
  });

  it('rejects empty, malformed and impossible days', () => {
    expect(utcDayStart('')).toBeNull();
    expect(utcDayStart('31.01.2026')).toBeNull();
    expect(utcDayStart('2026-02-30')).toBeNull();
  });

  it('reads today in UTC whatever the local zone is', () => {
    expect(utcToday(new Date('2026-10-24T23:30:00Z'))).toBe('2026-10-24');
  });
});
