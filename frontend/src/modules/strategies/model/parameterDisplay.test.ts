import { parameterReadableName, parameterValueDisplay } from './parameterDisplay';

describe('parameter display', () => {
  it('names known parameters and keeps unknown names', () => {
    expect(parameterReadableName('drop')).toBe('Снижение для усреднения');
    expect(parameterReadableName('custom')).toBe('custom');
  });

  it('shows fractions as percent when the parameter is marked as percent', () => {
    expect(parameterValueDisplay('0.015', 'percent')).toBe('1.5%');
    expect(parameterValueDisplay('0.1', 'percent')).toBe('10%');
  });

  it('keeps other values as they are', () => {
    expect(parameterValueDisplay('0.001', 'none')).toBe('0.001');
    expect(parameterValueDisplay('100', 'quote')).toBe('100');
    expect(parameterValueDisplay('20')).toBe('20');
  });
});
