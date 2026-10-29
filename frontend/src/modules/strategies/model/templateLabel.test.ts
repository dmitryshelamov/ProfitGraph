import { strategyTemplateLabel } from './templateLabel';

describe('strategyTemplateLabel', () => {
  it('names known templates', () => {
    expect(strategyTemplateLabel('LadderSma')).toBe('Ladder SMA');
    expect(strategyTemplateLabel('RsiReversion')).toBe('Отскок RSI');
    expect(strategyTemplateLabel('LevelTrade')).toBe('Сделка по уровням');
  });

  it('treats a missing or unknown marker as the user’s own strategy', () => {
    expect(strategyTemplateLabel(null)).toBe('Своя стратегия');
    expect(strategyTemplateLabel('Removed')).toBe('Своя стратегия');
  });
});
