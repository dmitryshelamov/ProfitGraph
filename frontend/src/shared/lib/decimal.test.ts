import { fractionToPercent, isDecimalString } from './decimal';

describe('decimal strings', () => {
  it('accepts canonical decimals only', () => {
    expect(isDecimalString('0.25')).toBe(true);
    expect(isDecimalString('-12')).toBe(true);
    expect(isDecimalString('01')).toBe(false);
    expect(isDecimalString('1e3')).toBe(false);
    expect(isDecimalString('1.')).toBe(false);
  });

  it('converts fractions to percent exactly', () => {
    expect(fractionToPercent('0.001')).toBe('0.1');
    expect(fractionToPercent('0.2')).toBe('20');
    expect(fractionToPercent('3')).toBe('300');
    expect(fractionToPercent('-0.015')).toBe('-1.5');
    expect(fractionToPercent('0.1000000000000000000000000001')).toBe(
      '10.00000000000000000000000001',
    );
  });

  it('rejects a value that is not a decimal string', () => {
    expect(fractionToPercent('abc')).toBeNull();
  });
});
