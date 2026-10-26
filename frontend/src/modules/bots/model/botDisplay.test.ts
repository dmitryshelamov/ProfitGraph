import { describe, expect, it } from 'vitest';

import {
  atPlaces,
  fractionLabel,
  parameterInputText,
  parameterStoredValue,
  stepPlaces,
  withAsset,
} from './botDisplay';

describe('botDisplay', () => {
  it('counts the decimal places of a step', () => {
    expect(stepPlaces('0.01')).toBe(2);
    expect(stepPlaces('1')).toBe(0);
    expect(stepPlaces('0.00010')).toBe(4);
    expect(stepPlaces('')).toBeNull();
  });

  it('rounds to the venue places and keeps unknown places as they are', () => {
    expect(atPlaces('0.03602484472049689', 4)).toBe('0.036');
    expect(atPlaces('70.0000104', 6)).toBe('70.00001');
    expect(atPlaces('1.5', null)).toBe('1.5');
  });

  it('adds the asset when it is known', () => {
    expect(withAsset('1.5', 'USDT')).toBe('1.5 USDT');
    expect(withAsset('1.5', null)).toBe('1.5');
  });

  it('shows a fraction as percent and types a percent parameter as percent', () => {
    expect(fractionLabel('0.001')).toBe('0.1%');
    expect(fractionLabel('0')).toBe('0%');
    expect(parameterInputText('0.075', true)).toBe('7.5');
    expect(parameterInputText('50', false)).toBe('50');
    expect(parameterStoredValue('7.5', true)).toBe('0.075');
    expect(parameterStoredValue(' 50 ', false)).toBe('50');
  });
});
