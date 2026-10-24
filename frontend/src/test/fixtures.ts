import type { components } from '@/shared/api/generated/schema';

type Schemas = components['schemas'];

export const owner: Schemas['UserResponse'] = {
  id: '6f1c2c55-7c1b-4d43-9a43-6d2f8f6b1a01',
  login: 'owner',
  role: 'Owner',
  status: 'Active',
};

export const ladderDefinition: Schemas['StrategyDefinitionResponse'] = {
  parameters: [
    { name: 'buyAmount', valueType: 'QuoteAmount', defaultValue: '25' },
    { name: 'drop', valueType: 'Scalar', defaultValue: '0.015', displayUnit: 'percent' },
  ],
  values: [],
  blocks: [],
  wires: [],
  templateKind: 'LadderSma',
};

export const layout: Schemas['GraphLayoutResponse'] = { positions: {} };

export const strategy: Schemas['StrategyResponse'] = {
  id: '0b8f7a43-1f55-4b0b-9f5e-2b6b0e5c7d11',
  name: 'Ladder на BTC',
  currentVersion: 2,
  createdAt: '2026-10-20T08:00:00Z',
  updatedAt: '2026-10-24T09:30:00Z',
  definition: ladderDefinition,
  layout,
};

export const summary: Schemas['StrategySummaryResponse'] = {
  id: strategy.id,
  name: strategy.name,
  currentVersion: 2,
  updatedAt: strategy.updatedAt,
  templateKind: 'LadderSma',
  latestRun: null,
};

export const template: Schemas['StrategyTemplateResponse'] = {
  kind: 'LadderSma',
  name: 'Ladder SMA',
  description: 'Лестница с фильтром SMA',
  definition: ladderDefinition,
  layout,
};
