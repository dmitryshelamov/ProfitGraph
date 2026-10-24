import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { components } from '@/shared/api/generated/schema';
import { completedRun, job } from '@/test/backtestFixtures';
import { fakeApi, json } from '@/test/fakeApi';
import { hub } from '@/test/fakeHub';
import { owner, strategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

type Schemas = components['schemas'];

const optimizationId = '4d7c6e2a-9a1f-4d5e-8b8f-2a1b3c4d5e6f';
const path = `/api/optimizations/${optimizationId}`;

const optimization: Schemas['OptimizationRunResponse'] = {
  id: optimizationId,
  strategyId: strategy.id,
  strategyVersion: 2,
  status: 'Completed',
  createdAt: '2026-10-25T08:00:00Z',
  configuration: {
    backtest: completedRun.configuration,
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
  counters: { completed: 2, rejectedByConstraint: 1, rejectedByTemplate: 0, failed: 0 },
  best: { trialIndex: 1, objective: '7.5' },
  finalists: ['b1d2c3e4-0000-4000-8000-000000000001'],
  walkForwardSummary: null,
};

function trial(
  sequence: number,
  change: Partial<Schemas['OptimizationTrialResponse']> = {},
): Schemas['OptimizationTrialResponse'] {
  return {
    sequence,
    trialIndex: sequence - 1,
    parameters: { drop: `0.0${sequence}` },
    status: 'completed',
    failureCode: null,
    objective: `${sequence}.5`,
    returnPercent: `${sequence}.5`,
    totalPnl: '10',
    maxDrawdownPercent: '2',
    executions: 4,
    elapsedMs: 3,
    stage: 1,
    windowIndex: 0,
    neighborhood: null,
    ...change,
  };
}

function base() {
  return {
    'GET /api/auth/me': () => json(owner),
    [`GET /api/strategies/${strategy.id}`]: () => json(strategy),
  };
}

describe('optimization pages', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    hub.reset();
  });

  it('shows a finished optimization: counters, best trial, finalists, every trial and its settings', async () => {
    fakeApi({
      ...base(),
      [`GET ${path}`]: () => json(optimization),
      [`GET /api/jobs/${optimizationId}`]: () =>
        json(job('Completed', { id: optimizationId, kind: 'optimization' })),
      [`GET ${path}/trials?after=0&limit=500`]: () =>
        json({
          items: [trial(1), trial(2), trial(3, { status: 'rejected_by_constraint' })],
          next: null,
        }),
    });
    renderApp(`/optimizations/${optimizationId}`);

    const trials = await screen.findByRole('table', { name: 'Испытания' });
    expect(within(trials).getAllByRole('row')).toHaveLength(4);
    expect(within(trials).getByText('Вне ограничений')).toBeInTheDocument();
    expect(screen.getByText('Финалисты запущены')).toBeInTheDocument();
    expect(screen.getByText('3 из 3')).toBeInTheDocument();
    expect(screen.getByText('№2, цель 7.5')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Финалист 1' })).toHaveAttribute(
      'href',
      '/backtests/b1d2c3e4-0000-4000-8000-000000000001',
    );
    expect(screen.getByText('0.01 … 0.03, шаг 0.01')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Приостановить' })).not.toBeInTheDocument();
  });

  it('follows a running optimization live and pauses it', async () => {
    const running = {
      ...optimization,
      status: 'Running',
      stage: 'searching',
      finalists: [],
      best: null,
    };
    const requests = fakeApi({
      ...base(),
      [`GET ${path}`]: () => json(running),
      [`GET /api/jobs/${optimizationId}`]: () =>
        json(job('Running', { id: optimizationId, kind: 'optimization' })),
      [`GET ${path}/trials?after=0&limit=500`]: () => json({ items: [trial(1)], next: null }),
      [`POST ${path}/pause`]: () =>
        json(
          job('Running', { id: optimizationId, kind: 'optimization', pauseRequested: true }),
          202,
        ),
    });
    hub.cursor = { lastSequence: 1, status: 'Running' } as never;
    renderApp(`/optimizations/${optimizationId}`);

    await waitFor(() => expect(hub.subscriptions).toEqual([optimizationId]));
    hub.emit('OptimizationProgress', { optimizationId, trial: trial(2) });
    const live = await screen.findByRole('region', { name: 'Ход оптимизации' });
    await waitFor(() => expect(within(live).getAllByRole('row')).toHaveLength(3));

    await userEvent.click(screen.getByRole('button', { name: 'Приостановить' }));

    await waitFor(() =>
      expect(
        requests.some((request) => request.method === 'POST' && request.url.endsWith('/pause')),
      ).toBe(true),
    );
  });

  it('starts an optimization of the ranged parameters and opens it', async () => {
    let body: unknown = null;
    fakeApi({
      ...base(),
      'GET /api/market-data/venues': () =>
        json([{ id: 'bybit', name: 'Bybit', timeframes: ['1h'] }]),
      'POST /api/optimizations': async (request) => {
        body = await request.json();

        return json(job('Queued', { id: optimizationId, kind: 'optimization' }), 202);
      },
      [`GET ${path}`]: () => json({ ...optimization, status: 'Queued', stage: null }),
      [`GET /api/jobs/${optimizationId}`]: () =>
        json(job('Queued', { id: optimizationId, kind: 'optimization' })),
      [`GET ${path}/trials?after=0&limit=500`]: () => json({ items: [], next: null }),
    });
    renderApp(`/strategies/${strategy.id}/optimize`);

    const form = await screen.findByRole('form', { name: 'Настройка оптимизации' });
    await userEvent.click(within(form).getByLabelText(/Искать: .*%/));
    await userEvent.click(within(form).getByRole('button', { name: 'Запустить оптимизацию' }));

    await screen.findByRole('heading', { name: /Оптимизация/ });
    expect(body).toMatchObject({
      ranges: { drop: { min: '0.015', max: '0.015', step: '0.015' } },
      backtest: { parameters: { buyAmount: '25' } },
      method: 'auto',
    });
  });
});
