import { formatDateTime } from './dateTime';

describe('formatDateTime', () => {
  it('keeps an unparsable value as it is', () => {
    expect(formatDateTime('not a date')).toBe('not a date');
  });

  it('formats a date without time when asked', () => {
    const value = '2026-10-24T12:00:00Z';

    expect(formatDateTime(value, true)).toBe(
      new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'short', year: 'numeric' }).format(
        new Date(value),
      ),
    );
    expect(formatDateTime(value)).not.toBe(formatDateTime(value, true));
  });
});
