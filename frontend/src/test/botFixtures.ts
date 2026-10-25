import type { components } from '@/shared/api/generated/schema';

import { strategy } from './fixtures';

type Schemas = components['schemas'];

export const credential: Schemas['ExchangeCredentialResponse'] = {
  id: '3c0d6a1e-4b8f-4f0e-9a51-2f3a4b5c6d70',
  venue: 'bybit',
  label: 'Основной',
  apiKeyLast4: 'abcd',
  createdAt: '2026-10-24T08:00:00Z',
};

export const bot: Schemas['BotResponse'] = {
  id: '7a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d',
  strategyId: strategy.id,
  strategyVersionId: '9e8d7c6b-5a49-4382-9170-6f5e4d3c2b1a',
  credentialId: credential.id,
  venue: 'bybit',
  symbol: 'BTCUSDT',
  timeframe: '1h',
  parameters: { buyAmount: '25', drop: '0.015' },
  allocationCap: '100',
  maxOrderQuote: '20',
  dailyLossLimitQuote: '10',
  limitFeeRate: '0.001',
  marketFeeRate: '0.001',
  feeRatesReadAt: '2026-10-24T08:00:00Z',
  allowForeignOrders: false,
  desiredState: 'Stopped',
  runtimeStatus: 'Created',
  statusReason: null,
  diagnostics: [],
  restartAttempts: 0,
  nextRestartAt: null,
  hasRun: false,
  createdAt: '2026-10-24T08:00:00Z',
  updatedAt: '2026-10-24T08:00:00Z',
  sellerFees: null,
};

export const flatPosition: Schemas['BotPositionResponse'] = {
  baseTotal: '0',
  quoteTotal: '100',
  costBasis: '0',
  averageCost: null,
  realizedPnl: '0',
  openedAt: null,
  markPrice: null,
  markPriceObservedAt: null,
  markPriceSource: null,
  unrealizedPnl: null,
  cycles: [],
};
