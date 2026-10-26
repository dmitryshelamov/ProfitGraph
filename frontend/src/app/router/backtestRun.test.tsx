import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { completedRun, fill, job, observation, queuedRun, runId } from '@/test/backtestFixtures';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { hub } from '@/test/fakeHub';
import { owner, strategy, summary } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const runPath = `/api/backtests/${runId}`;

function base() {
  return {
    'GET /api/auth/me': () => json(owner),
    [`GET /api/strategies/${strategy.id}`]: () => json(strategy),
  };
}

describe('backtest run page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the results of a completed run: KPIs, chart, metrics, facts and its settings', async () => {
    fakeApi({
      ...base(),
      [`GET ${runPath}`]: () => json(completedRun),
      [`GET /api/jobs/${runId}`]: () => json(job('Completed', { completed: 24, total: 24 })),
      [`GET ${runPath}/chart`]: () =>
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
      [`GET ${runPath}/fills?after=0&limit=50`]: () => json({ items: [fill], next: 3 }),
      [`GET ${runPath}/fills?after=3&limit=50`]: () =>
        json({
          items: [{ ...fill, sequence: 5, fill: { ...fill.fill!, orderId: 9, side: 'Sell' } }],
          next: null,
        }),
      [`GET ${runPath}/diagnostics?after=0&limit=50`]: () =>
        json({
          items: [
            {
              sequence: 1,
              code: 'IndicatorsNeverReady',
              message: 'Индикаторы не успели прогреться.',
              nodeId: null,
              candleIndex: null,
              tag: null,
              rejectionReason: null,
            },
          ],
          next: null,
        }),
    });
    renderApp(`/backtests/${runId}`);

    const summaryList = await screen.findByLabelText('Итоги прогона');
    expect(within(summaryList).getByTitle('12.5')).toHaveTextContent('12.5 USDT');
    expect(within(summaryList).getByTitle('3.456789')).toHaveTextContent('3.46%');
    expect(screen.getByRole('heading', { name: 'BTCUSDT · 1 час' })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: strategy.name })).toBeInTheDocument();
    expect(await screen.findByRole('figure', { name: 'График прогона' })).toHaveAttribute(
      'data-points',
      '2',
    );

    expect(screen.getByRole('rowheader', { name: 'Отложенная выборка' })).toBeInTheDocument();
    expect(screen.getByTitle('-0.333333')).toHaveTextContent('-0.33');

    expect((await screen.findAllByTitle('101.25')).length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));
    expect(await screen.findByText('Продажа')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Диагностика' }));
    expect(await screen.findByText('Индикаторы не успели прогреться.')).toBeInTheDocument();

    expect(screen.getByText('лимитные 0.1%, рыночные 0.1%')).toBeInTheDocument();
    expect(screen.getByText('50 из 50 свечей')).toBeInTheDocument();
    expect(hub.subscriptions).toEqual([]);
  });

  it('follows a running run live and fills a gap in its messages from the stored observations', async () => {
    const requests = fakeApi({
      ...base(),
      [`GET ${runPath}`]: () =>
        json({
          ...queuedRun,
          status: 'Running',
          stage: 'replaying',
          history: completedRun.history,
        }),
      [`GET /api/jobs/${runId}`]: () => json(job('Running', { completed: 1, total: 24 })),
      [`GET ${runPath}/observations?after=0&limit=100`]: () =>
        json({ items: [observation(1)], next: null }),
      [`GET ${runPath}/observations?after=1&limit=100`]: () =>
        json({ items: [observation(2), observation(3)], next: null }),
    });
    hub.cursor = { lastSequence: 1, outcome: null };
    renderApp(`/backtests/${runId}`);

    expect(await screen.findByText('В реальном времени')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole('figure', { name: 'График прогона' })).toHaveAttribute(
        'data-points',
        '1',
      ),
    );
    hub.emit('BacktestProgress', { runId, observation: observation(4) });

    await waitFor(() =>
      expect(screen.getByRole('figure', { name: 'График прогона' })).toHaveAttribute(
        'data-points',
        '4',
      ),
    );
    expect(screen.getByText('1004.5', { exact: false })).toBeInTheDocument();
    expect(hub.subscriptions).toEqual([runId]);
    expect(
      requests
        .filter((request) => request.url.includes('/observations'))
        .map((request) => new URL(request.url).search),
    ).toEqual(['?after=0&limit=100', '?after=1&limit=100']);
    expect(screen.getByRole('button', { name: 'Отменить' })).toBeInTheDocument();
  });

  it('reports a missing run', async () => {
    fakeApi({
      ...base(),
      [`GET ${runPath}`]: () => problem(404, 'backtest_not_found'),
      [`GET /api/jobs/${runId}`]: () => problem(404, 'job_not_found'),
    });
    renderApp(`/backtests/${runId}`);

    expect(await screen.findByText('Прогон не найден.')).toBeInTheDocument();
  });
});

describe('run lists', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const listed = {
    id: runId,
    strategyId: strategy.id,
    strategyName: strategy.name,
    strategyVersion: 2,
    venue: 'bybit',
    symbol: 'BTCUSDT',
    timeframe: '1h',
    createdAt: '2026-10-24T10:00:00Z',
    status: 'Completed',
    outcome: 'completed',
    quoteAsset: 'USDT',
    totalPnl: '12.5',
    returnPercent: '1.25',
    maxDrawdownPercent: '3',
  };

  it('ranks completed runs in a quote currency and loads more', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/strategies': () => json([]),
      'GET /api/backtests?quote=USDT&limit=50&sort=return&order=desc': () =>
        json({ items: [listed], next: runId }),
      [`GET /api/backtests?quote=USDT&limit=50&sort=return&order=desc&after=${runId}`]: () =>
        json({
          items: [{ ...listed, id: 'second', symbol: 'ETHUSDT', returnPercent: '-2' }],
          next: null,
        }),
    });
    renderApp('/backtests');

    const table = await screen.findByRole('table');
    expect(within(table).getByText(strategy.name)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));
    expect(await within(table).findByText('ETHUSDT')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Показать ещё' })).not.toBeInTheDocument();
  });

  it('starts on the quote currency of the latest run', async () => {
    const older = { ...listed, id: 'older', createdAt: '2026-10-20T10:00:00Z', quoteAsset: 'USDT' };
    const latest = { ...listed, id: 'latest', symbol: 'TQBR:GAZP', quoteAsset: 'RUB' };
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/strategies': () =>
        json([
          { ...summary, latestRun: older },
          { ...summary, id: 'other', latestRun: latest },
        ]),
      'GET /api/backtests?quote=RUB&limit=50&sort=return&order=desc': () =>
        json({ items: [latest], next: null }),
    });
    renderApp('/backtests');

    expect(await screen.findByText('TQBR:GAZP')).toBeInTheDocument();
    expect(screen.getByLabelText('Валюта котировки')).toHaveValue('RUB');
  });

  it('sorts the ranking by a result column and picks runs to compare', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/strategies': () => json([]),
      'GET /api/backtests?quote=USDT&limit=50&sort=return&order=desc': () =>
        json({ items: [listed], next: null }),
      'GET /api/backtests?quote=USDT&limit=50&sort=drawdown&order=desc': () =>
        json({ items: [{ ...listed, id: 'deep', symbol: 'SOLUSDT' }], next: null }),
    });
    sessionStorage.clear();
    renderApp('/backtests');

    await screen.findByText('BTCUSDT');
    await userEvent.click(screen.getByRole('button', { name: 'Просадка, %' }));
    expect(await screen.findByText('SOLUSDT')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Просадка, % ↓' })).toHaveAttribute(
      'aria-sort',
      'descending',
    );

    await userEvent.click(screen.getByRole('checkbox', { name: 'Сравнить прогон SOLUSDT' }));
    const bar = screen.getByRole('complementary', { name: 'Сравнение' });
    expect(within(bar).getByText('Выбрано для сравнения: 1')).toBeInTheDocument();
    expect(within(bar).getByRole('link', { name: 'Сравнить' })).toHaveAttribute(
      'href',
      '/compare?runs=deep',
    );
  });

  it('lists a strategy’s runs on its page and its latest run in the strategy list', async () => {
    fakeApi({
      ...base(),
      [`GET /api/strategies/${strategy.id}/versions`]: () => json([]),
      [`POST /api/strategies/${strategy.id}/validate?version=2`]: () =>
        json({ isValid: true, version: 2, diagnostics: [] }),
      [`GET /api/strategies/${strategy.id}/backtests?limit=20`]: () =>
        json({ items: [listed], next: null }),
      'GET /api/strategies': () => json([{ ...summary, latestRun: listed }]),
    });
    const { history } = renderApp(`/strategies/${strategy.id}`);

    expect(await screen.findByRole('heading', { name: 'Прогоны' })).toBeInTheDocument();
    expect(await screen.findByText('BTCUSDT')).toBeInTheDocument();
    await userEvent.click(
      within(screen.getByRole('navigation', { name: 'Разделы' })).getByRole('link', {
        name: 'Стратегии',
      }),
    );
    await waitFor(() => expect(history.location.pathname).toBe('/strategies'));
    const header = await screen.findByRole(
      'columnheader',
      { name: 'Последний прогон' },
      { timeout: 5000 },
    );
    const latest = within(header.closest('table')!).getByTitle('1.25');
    expect(latest).toHaveTextContent('1.25%');
    expect(latest.closest('a')).toHaveAttribute('href', `/backtests/${runId}`);
  });
});
