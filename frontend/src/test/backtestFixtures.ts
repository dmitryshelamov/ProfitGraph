import type { components } from '@/shared/api/generated/schema';

import { strategy } from './fixtures';

type Schemas = components['schemas'];

export const runId = '0f5c8a3e-2b7d-4f21-9c4e-3a1d2b6c7e80';

const sample = (
  observedCandles: number,
  returnPercent: string,
): Schemas['SampleMetricsResponse'] => ({
  startIndex: 0,
  endExclusive: observedCandles,
  observedCandles,
  boundaryEquity: '1000',
  endEquity: '1012.5',
  totalPnl: '12.5',
  realizedPnl: '12.5',
  unrealizedPnl: '0',
  returnPercent,
  feesBase: '0.000012',
  feesQuote: '0.15',
  maxDrawdownPercent: '3.456789',
  executions: 4,
});

export const queuedRun: Schemas['BacktestRunResponse'] = {
  id: runId,
  strategyId: strategy.id,
  strategyVersion: 2,
  status: 'Queued',
  createdAt: '2026-10-24T10:00:00Z',
  configuration: {
    venue: 'bybit',
    symbol: 'BTCUSDT',
    timeframe: '1h',
    from: '2026-01-01T00:00:00Z',
    to: '2026-01-02T00:00:00Z',
    observedAt: '2026-10-24T10:00:00Z',
    parameters: { buyAmount: '25', drop: '0.015' },
    startingCapital: '1000',
    slippage: '0',
    limitFeeRate: '0.001',
    marketFeeRate: '0.001',
    outOfSampleFraction: '0.25',
    warmupCandles: 50,
  },
  stage: null,
  history: null,
  result: null,
  ledgerCount: 0,
  candleCount: 0,
  diagnosticCount: 0,
  chartBucketSize: null,
};

export const completedRun: Schemas['BacktestRunResponse'] = {
  ...queuedRun,
  status: 'Completed',
  stage: 'replaying',
  history: {
    identity: 'a'.repeat(64),
    historyFrom: '2025-12-29T22:00:00Z',
    from: '2026-01-01T00:00:00Z',
    to: '2026-01-02T00:00:00Z',
    warmupCount: 50,
    replayCount: 24,
    rulesSnapshotId: '3c2f6a1b-0d4e-4b8a-9f7c-1e2d3c4b5a69',
    baseAsset: 'BTC',
    quoteAsset: 'USDT',
    rules: {
      quantityStep: '0.000001',
      priceTick: '0.01',
      minQuantity: '0',
      minNotional: '5',
      basePrecision: 6,
      quotePrecision: 8,
    },
  },
  result: {
    outcome: 'completed',
    processedCandles: 24,
    equity: '1012.5',
    totalPnl: '12.5',
    failureCode: null,
    failureMessage: null,
    metrics: {
      full: sample(24, '1.25'),
      inSample: sample(18, '1.6'),
      outOfSample: sample(6, '-0.333333'),
    },
  },
  ledgerCount: 8,
  candleCount: 24,
  diagnosticCount: 1,
  chartBucketSize: 1,
};

export function job(
  status: string,
  extra: Partial<Schemas['BackgroundJobResponse']> = {},
): Schemas['BackgroundJobResponse'] {
  return {
    id: runId,
    kind: 'backtest',
    status,
    completed: 0,
    total: null,
    cancelRequested: false,
    pauseRequested: false,
    createdAt: '2026-10-24T10:00:00Z',
    startedAt: null,
    finishedAt: null,
    result: null,
    failure: null,
    ...extra,
  };
}

export function observation(sequence: number): Schemas['BacktestObservationResponse'] {
  const hour = String(sequence - 1).padStart(2, '0');

  return {
    sequence,
    openTime: `2026-01-01T${hour}:00:00Z`,
    candleIndex: sequence - 1,
    phase: 'Close',
    marketPrice: '105',
    open: '100',
    high: '110',
    low: '90',
    close: '105',
    volume: '1',
    equity: `${1000 + sequence}.5`,
    unrealizedPnl: '0',
    totalPnl: `${sequence}.5`,
    realizedPnl: '0',
    quoteTotal: '1000',
    quoteReserved: '0',
    baseTotal: '0',
    baseReserved: '0',
    costBasis: '0',
    feesBase: '0',
    feesQuote: '0',
    lastBuyPrice: null,
    activeOrders: 0,
    ledger: [],
    diagnostics: [],
  };
}

export const fill: Schemas['BacktestLedgerEntryResponse'] = {
  sequence: 3,
  kind: 'Filled',
  candleIndex: 2,
  candleOpenTime: '2026-01-01T02:00:00Z',
  phase: 'Open',
  marketPrice: '101.25',
  order: null,
  fill: {
    orderId: 7,
    tag: 'entry',
    side: 'Buy',
    type: 'Market',
    price: '101.25',
    grossBase: '0.246913',
    baseChange: '0.246666',
    quoteChange: '-25',
    fee: '0.000247',
    feeAsset: 'Base',
    realizedPnl: '0',
  },
  rejection: null,
};
