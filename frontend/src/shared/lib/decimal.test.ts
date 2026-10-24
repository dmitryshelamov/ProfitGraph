import {
  fractionToPercent,
  isBackendDecimal,
  isDecimalString,
  percentToFraction,
  roundDecimal,
} from './decimal';

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

  it('converts typed percent to a stored fraction exactly', () => {
    expect(percentToFraction('0.1')).toBe('0.001');
    expect(percentToFraction('20')).toBe('0.2');
    expect(percentToFraction(' 1.5 ')).toBe('0.015');
    expect(percentToFraction('-0.5')).toBe('-0.005');
    expect(percentToFraction('0')).toBe('0');
    expect(percentToFraction('1,5')).toBeNull();
  });

  it('accepts only values the API decimal can hold', () => {
    expect(isBackendDecimal('79228162514264337593543950335.0')).toBe(true);
    expect(isBackendDecimal('79228162514264337593543950336')).toBe(false);
    expect(isBackendDecimal('7922816251426433759354395033.6')).toBe(false);
    expect(isBackendDecimal('abc')).toBe(false);
  });
});

describe('roundDecimal', () => {
  it('rounds half away from zero without floating point', () => {
    expect(roundDecimal('1.005', 2)).toBe('1.01');
    expect(roundDecimal('-1.005', 2)).toBe('-1.01');
    expect(roundDecimal('2.344999999999999999999', 2)).toBe('2.34');
    expect(roundDecimal('0.004', 2)).toBe('0');
    expect(roundDecimal('-0.004', 2)).toBe('0');
  });

  it('keeps shorter values and rejects malformed ones', () => {
    expect(roundDecimal('12.5', 2)).toBe('12.5');
    expect(roundDecimal('7', 2)).toBe('7');
    expect(roundDecimal('1e3', 2)).toBeNull();
  });
});
