import { botPageSearch } from './botPageSearch';

describe('bot page search', () => {
  it('keeps a known timeframe and a tab other than the default one', () => {
    expect(botPageSearch({ timeframe: '4h', tab: 'settings' })).toEqual({
      timeframe: '4h',
      tab: 'settings',
    });
    expect(botPageSearch({ timeframe: '7m', tab: 'orders' })).toEqual({});
    expect(botPageSearch({ tab: 'nothing', extra: 1 })).toEqual({});
  });
});
