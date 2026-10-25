import {
  steamBotSettingsBody,
  steamBotSettingsDraft,
  steamBotSettingsFallback,
} from './steamBotSettings';

describe('Steam bot settings', () => {
  it('start from the venue defaults', () => {
    expect(
      steamBotSettingsDraft({
        settlementHoldDays: 8,
        marketBuySlippage: '0.02',
        marketBuyDeadlineSeconds: 300,
      }),
    ).toEqual({
      settlementHoldDays: '8',
      marketBuySlippage: '0.02',
      marketBuyDeadlineSeconds: '300',
    });
    expect(steamBotSettingsDraft(null)).toEqual(steamBotSettingsFallback);
  });

  it('send whole numbers as numbers and the slippage as exact text', () => {
    expect(
      steamBotSettingsBody({
        settlementHoldDays: ' 7 ',
        marketBuySlippage: ' 0.05 ',
        marketBuyDeadlineSeconds: '120',
      }),
    ).toEqual({ settlementHoldDays: 7, marketBuySlippage: '0.05', marketBuyDeadlineSeconds: 120 });
    expect(
      steamBotSettingsBody({ ...steamBotSettingsFallback, settlementHoldDays: '1.5' })
        .settlementHoldDays,
    ).toBe('1.5');
  });
});
