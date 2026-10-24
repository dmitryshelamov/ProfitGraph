import { comparisonSearch, parseComparison } from './comparisonItems';

const run = '0f5c8a3e-2b7d-4f21-9c4e-3a1d2b6c7e80';
const optimization = '4d7c6e2a-9a1f-4d5e-8b8f-2a1b3c4d5e6f';

describe('comparison address', () => {
  it('writes runs and trials and reads them back', () => {
    const items = [
      { kind: 'run', id: run },
      { kind: 'trial', optimizationId: optimization, windowIndex: 2, trialIndex: 14 },
    ] as const;

    expect(comparisonSearch(items)).toEqual({ runs: run, trials: `${optimization}:2:14` });
    expect(parseComparison(comparisonSearch(items))).toEqual(items);
  });

  it('skips malformed entries and keeps at most six items', () => {
    const runs = Array.from({ length: 8 }, () => run).join(',');

    expect(
      parseComparison({ runs: `${runs},not-a-run`, trials: `${optimization}:x:1` }),
    ).toHaveLength(6);
    expect(parseComparison({ trials: `${optimization}:0:-1,bad` })).toEqual([]);
  });
});
