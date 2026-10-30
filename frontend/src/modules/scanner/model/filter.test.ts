import { applyView, type ScanView } from './filter';
import type { ScanRow } from './types';

function row(symbol: string, fields: Partial<ScanRow> = {}): ScanRow {
  return {
    symbol,
    price: null,
    change5m: null,
    change15m: null,
    change1h: null,
    change4h: null,
    change24h: null,
    turnover1h: null,
    turnover24h: null,
    volumeSpike: null,
    volumeSpike5m: null,
    volumeSpike15m: null,
    natr24h: null,
    efficiencyRatio24h: null,
    distanceToHigh24h: null,
    distanceToLow24h: null,
    distanceToHigh7d: null,
    distanceToLow7d: null,
    breakout4h: null,
    breakdown4h: null,
    range4h: null,
    resistance: null,
    support: null,
    distanceToResistance: null,
    distanceToSupport: null,
    rewardRisk: null,
    trendEma50: null,
    emaSpread20x50: null,
    rsi1h: null,
    marketTrend: null,
    spread: null,
    listingAgeDays: null,
    stale: false,
    incomplete: false,
    ...fields,
  };
}

const byChange: ScanView = {
  conditions: [],
  sortMetric: 'Change1h',
  sortDescending: true,
  hideStale: false,
};

describe('applyView', () => {
  it('keeps rows within inclusive bounds and drops empty metrics', () => {
    const rows = [
      row('AUSDT', { change1h: '3' }),
      row('BUSDT', { change1h: '2.9999999999999999999' }),
      row('CUSDT', { change1h: '10' }),
      row('DUSDT'),
    ];

    const kept = applyView(rows, {
      ...byChange,
      conditions: [{ metric: 'Change1h', minimum: '3', maximum: '10' }],
    });

    expect(kept.map((item) => item.symbol)).toEqual(['CUSDT', 'AUSDT']);
  });

  it('sorts exactly, empty values last, and hides stale pairs when asked', () => {
    const rows = [
      row('AUSDT', { turnover24h: '1000000000000000000000.1' }),
      row('BUSDT', { turnover24h: '1000000000000000000000.2', stale: true }),
      row('CUSDT'),
      row('DUSDT', { turnover24h: '5' }),
    ];
    const view: ScanView = {
      conditions: [],
      sortMetric: 'Turnover24h',
      sortDescending: true,
      hideStale: false,
    };

    expect(applyView(rows, view).map((item) => item.symbol)).toEqual([
      'BUSDT',
      'AUSDT',
      'DUSDT',
      'CUSDT',
    ]);
    expect(
      applyView(rows, { ...view, sortDescending: false, hideStale: true }).map(
        (item) => item.symbol,
      ),
    ).toEqual(['DUSDT', 'AUSDT', 'CUSDT']);
  });

  it('compares the listing age as a number of days', () => {
    const rows = [row('NEWUSDT', { listingAgeDays: 2 }), row('OLDUSDT', { listingAgeDays: 6 })];

    const kept = applyView(rows, {
      ...byChange,
      conditions: [{ metric: 'ListingAgeDays', minimum: '', maximum: '3' }],
    });

    expect(kept.map((item) => item.symbol)).toEqual(['NEWUSDT']);
  });
});
