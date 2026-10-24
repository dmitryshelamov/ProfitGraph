import { screen, within } from '@testing-library/react';

import type { components } from '@/shared/api/generated/schema';
import { completedRun, runId } from '@/test/backtestFixtures';
import { fakeApi, json } from '@/test/fakeApi';
import { owner, strategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

type Schemas = components['schemas'];

const optimizationId = '4d7c6e2a-9a1f-4d5e-8b8f-2a1b3c4d5e6f';

const trial: Schemas['OptimizationTrialResponse'] = {
  sequence: 2,
  trialIndex: 1,
  parameters: { drop: '0.02' },
  status: 'completed',
  failureCode: null,
  objective: '7.5',
  returnPercent: '7.5',
  totalPnl: '75',
  maxDrawdownPercent: '4',
  executions: 6,
  elapsedMs: 3,
  stage: 1,
  windowIndex: 0,
  neighborhood: null,
};

const optimization: Schemas['OptimizationRunResponse'] = {
  id: optimizationId,
  strategyId: strategy.id,
  strategyVersion: 2,
  status: 'Completed',
  createdAt: '2026-10-25T08:00:00Z',
  configuration: {
    backtest: { ...completedRun.configuration, parameters: { buyAmount: '25' } },
    ranges: { drop: { min: '0.01', max: '0.03', step: '0.01' } },
    method: 'grid',
    objective: 'in_sample_return_percent',
    budget: 2000,
    seed: '42',
    finalistCount: 3,
    minExecutions: 1,
    maxDrawdownPercent: '50',
    walkForward: null,
  },
  stage: 'starting_finalists',
  history: null,
  plannedTrials: 3,
  counters: { completed: 3, rejectedByConstraint: 0, rejectedByTemplate: 0, failed: 0 },
  best: { trialIndex: 1, objective: '7.5' },
  finalists: [],
  walkForwardSummary: null,
};

describe('comparison page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows a run and an optimization trial side by side with the run’s equity', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      [`GET /api/backtests/${runId}`]: () => json(completedRun),
      [`GET /api/backtests/${runId}/chart`]: () =>
        json({
          bucketSize: 1,
          points: [0, 1].map((index) => ({
            firstSequence: index + 1,
            lastSequence: index + 1,
            firstOpenTime: `2026-01-01T0${index}:00:00Z`,
            lastOpenTime: `2026-01-01T0${index}:00:00Z`,
            open: '100',
            high: '110',
            low: '90',
            close: '105',
            volume: '1',
            equity: '1000',
            minEquity: '1000',
            maxEquity: '1000',
            totalPnl: '0',
            buyFills: 0,
            sellFills: 0,
          })),
        }),
      [`GET /api/optimizations/${optimizationId}`]: () => json(optimization),
      [`GET /api/optimizations/${optimizationId}/trials/1?window=0`]: () => json(trial),
    });
    renderApp(`/compare?runs=${runId}&trials=${optimizationId}:0:1&trials-ignored=x`);

    const table = await screen.findByRole('table', { name: 'Сравнение' });
    expect(await within(table).findByRole('link', { name: 'Испытание №2' })).toHaveAttribute(
      'href',
      `/optimizations/${optimizationId}`,
    );
    expect(within(table).getByRole('link', { name: /^Прогон BTCUSDT/ })).toHaveAttribute(
      'href',
      `/backtests/${runId}`,
    );
    const objective = within(table).getByRole('row', { name: /Цель испытания/ });
    expect(within(objective).getByText('7.5')).toBeInTheDocument();
    const inSample = within(table).getByRole('row', { name: /Обучающая часть: доходность/ });
    expect(within(inSample).getByTitle('1.6')).toBeInTheDocument();
    expect(within(inSample).getByTitle('7.5')).toBeInTheDocument();
    const drop = within(table).getByRole('row', { name: /Снижение для усреднения/ });
    expect(within(drop).getByText('0.02')).toBeInTheDocument();
    expect(
      await screen.findByRole('figure', { name: 'Капитал сравниваемых прогонов' }),
    ).toHaveAttribute('data-lines', '1');
  });

  it('explains how to pick items when nothing is chosen', async () => {
    fakeApi({ 'GET /api/auth/me': () => json(owner) });
    renderApp('/compare');

    expect(await screen.findByText(/Отметьте прогоны/)).toBeInTheDocument();
  });
});
