import {
  accessLevelLabel,
  accountIdentifier,
  accountKindLabel,
  ageLabel,
  isStale,
} from './botAccount';

describe('bot account display', () => {
  const now = Date.parse('2026-10-28T12:00:00Z');

  it('names the account by its venue, preferring what the venue reported', () => {
    expect(
      accountIdentifier({
        venue: 'bybit',
        accountPublicId: null,
        identity: {
          accountId: '24617703',
          name: null,
          kind: null,
          accessLevel: null,
          readOnly: null,
          permissions: [],
          expiresAt: null,
          readAt: '2026-10-28T11:00:00Z',
        },
      }),
    ).toEqual({ label: 'UID', value: '24617703' });
    expect(
      accountIdentifier({ venue: 'steam', accountPublicId: '76561190000000001', identity: null }),
    ).toEqual({ label: 'SteamID', value: '76561190000000001' });
    expect(accountIdentifier({ venue: 'bybit', accountPublicId: null, identity: null })).toBeNull();
  });

  it('labels kinds and access levels in Russian and keeps unknown codes', () => {
    expect(accountKindLabel('iis')).toBe('ИИС');
    expect(accessLevelLabel('read_only')).toBe('только чтение');
    expect(accessLevelLabel('custom')).toBe('custom');
  });

  it('tells the age in seconds, minutes, hours and days, and a snapshot older than 15 minutes is stale', () => {
    expect(ageLabel('2026-10-28T11:59:30Z', now)).toBe('30 с назад');
    expect(ageLabel('2026-10-28T11:55:00Z', now)).toBe('5 мин назад');
    expect(ageLabel('2026-10-28T09:00:00Z', now)).toBe('3 ч назад');
    expect(ageLabel('2026-10-26T12:00:00Z', now)).toBe('2 дн назад');
    expect(isStale('2026-10-28T11:50:00Z', now)).toBe(false);
    expect(isStale('2026-10-28T11:40:00Z', now)).toBe(true);
  });
});
