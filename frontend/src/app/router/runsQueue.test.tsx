import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { runId } from '@/test/backtestFixtures';
import { fakeApi, json } from '@/test/fakeApi';
import { owner, strategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const activeJob = {
  kind: 'backtest',
  lane: 'main',
  status: 'Queued',
  completed: 0,
  total: null,
  cancelRequested: false,
  pauseRequested: false,
  createdAt: '2026-10-29T10:00:00Z',
  startedAt: null,
  queuePosition: null,
  strategyId: strategy.id,
  strategyName: 'Ladder',
  strategyVersion: 7,
  venue: 'bybit',
  symbol: 'BTCUSDT',
  timeframe: '1h',
  method: null,
  optimizationId: null,
};

const jobs = [
  {
    ...activeJob,
    id: 'run-a',
    status: 'Running',
    completed: 64,
    total: 100,
    startedAt: '2026-10-29T10:01:00Z',
  },
  { ...activeJob, id: 'run-b', symbol: 'ETHUSDT', queuePosition: 3 },
  {
    ...activeJob,
    id: 'download-c',
    kind: 'historical_download',
    strategyId: null,
    strategyName: null,
    strategyVersion: null,
    symbol: 'SOLUSDT',
    timeframe: '5m',
    queuePosition: 4,
  },
  {
    ...activeJob,
    id: 'opt-d',
    kind: 'optimization',
    lane: 'optimization',
    status: 'Running',
    completed: 412,
    total: 1000,
    method: 'tpe',
    symbol: 'SOLUSDT',
    startedAt: '2026-10-29T09:00:00Z',
  },
  {
    ...activeJob,
    id: 'opt-e',
    kind: 'optimization',
    lane: 'optimization',
    status: 'Paused',
    method: 'grid',
  },
];

const facets = {
  quotes: [
    { value: 'USDT', count: 12 },
    { value: 'RUB', count: 3 },
  ],
  statuses: [
    { value: 'completed', count: 15 },
    { value: 'failed', count: 2 },
  ],
  origins: [
    { value: 'manual', count: 14 },
    { value: 'optimization', count: 3 },
  ],
  venues: [{ value: 'bybit', count: 17 }],
  timeframes: [{ value: '1h', count: 17 }],
  strategies: [{ id: strategy.id, name: 'Ladder', count: 17 }],
};

const listed = {
  id: runId,
  strategyId: strategy.id,
  strategyName: 'Ladder',
  strategyVersion: 7,
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
  optimizationId: null,
};

const optimization = {
  id: 'opt-1',
  strategyId: strategy.id,
  strategyName: 'Ladder',
  strategyVersion: 7,
  createdAt: '2026-10-29T09:00:00Z',
  venue: 'bybit',
  symbol: 'SOLUSDT',
  timeframe: '15m',
  method: 'tpe',
  status: 'Running',
  executedTrials: 412,
  plannedTrials: 1000,
  windowCount: 6,
  bestObjective: '1.8421',
};

function routes(active: { jobs: unknown[]; workerOnline: boolean } = { jobs, workerOnline: true }) {
  return {
    'GET /api/auth/me': () => json(owner),
    'GET /api/strategies': () => json([]),
    'GET /api/jobs/active': () => json(active),
    'GET /api/backtests/facets': () => json(facets),
    'GET /api/market-data/venues': () =>
      json([{ id: 'bybit', name: 'Bybit', timeframes: ['15m', '1h'] }]),
    'GET /api/backtests': () => json({ items: [listed], next: null }),
    'GET /api/optimizations': () => json({ items: [optimization], next: null }),
  };
}

function lastQuery(requests: Request[], path: string) {
  const url = new URL(
    requests.filter((request) => new URL(request.url).pathname === path).at(-1)!.url,
  );

  return Object.fromEntries(url.searchParams);
}

describe('runs page queue and filters', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows what runs and waits in each Worker lane, and the header counts it on every screen', async () => {
    const requests = fakeApi({
      ...routes(),
      'POST /api/jobs/run-b/cancel': () =>
        json({ ...activeJob, id: 'run-b', status: 'Cancelled' }, 202),
    });
    renderApp('/backtests');

    expect(
      await screen.findByRole('link', {
        name: 'Фоновые задачи: в работе 2 · в очереди 2 · на паузе 1',
      }),
    ).toHaveAttribute('href', '/backtests');
    const main = await screen.findByRole('region', { name: 'Прогоны и загрузки' });
    expect(within(main).getByText('в очереди 2')).toBeInTheDocument();
    expect(within(main).getByRole('progressbar', { name: 'Ход: Прогон' })).toHaveAttribute(
      'aria-valuenow',
      '64',
    );
    expect(within(main).getAllByRole('link', { name: 'Прогон' })[0]).toHaveAttribute(
      'href',
      '/backtests/run-a',
    );
    const queue = within(main).getByRole('list', { name: 'Очередь: Прогоны и загрузки' });
    const places = within(queue).getAllByRole('listitem');
    expect(
      places.map(
        (place) => within(place).getByTitle('Место среди всех задач этой очереди').textContent,
      ),
    ).toEqual(['№3', '№4']);
    expect(within(places[1]!).getByRole('link', { name: 'Загрузка истории' })).toHaveAttribute(
      'href',
      '/historical-data',
    );

    const optimizations = screen.getByRole('region', { name: 'Оптимизации' });
    expect(within(optimizations).getByText(/412 из 1000/)).toBeInTheDocument();
    expect(within(optimizations).getByText('на паузе')).toBeInTheDocument();
    expect(within(optimizations).getByText('очередь пуста')).toBeInTheDocument();

    await userEvent.click(
      within(places[0]!).getByRole('button', { name: 'Отменить: Прогон ETHUSDT' }),
    );
    await waitFor(() =>
      expect(
        requests.some(
          (request) => request.method === 'POST' && request.url.endsWith('/api/jobs/run-b/cancel'),
        ),
      ).toBe(true),
    );
  });

  it('warns when queued jobs wait for a Worker that does not answer', async () => {
    fakeApi(routes({ jobs: [jobs[1]], workerOnline: false }));
    renderApp('/backtests');

    expect(
      await screen.findByRole('link', { name: 'Фоновые задачи: в очереди 1, воркер не отвечает' }),
    ).toBeInTheDocument();
    expect(await screen.findByRole('alert')).toHaveTextContent('Воркер не отвечает');
  });

  it('hides the header count and says the queues are empty when nothing is active', async () => {
    fakeApi(routes({ jobs: [], workerOnline: true }));
    renderApp('/backtests');

    expect(
      await screen.findByText('Сейчас ничего не выполняется и не ждёт в очереди.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Фоновые задачи/ })).not.toBeInTheDocument();
  });

  it('filters runs, keeps the filters in the address and lists failed runs by start time', async () => {
    const requests = fakeApi(routes());
    const { history } = renderApp('/backtests');

    await screen.findByText('BTCUSDT', { selector: 'td' });
    expect(lastQuery(requests, '/api/backtests')).toEqual({
      quote: 'USDT',
      limit: '50',
      sort: 'return',
      order: 'desc',
    });

    await userEvent.type(screen.getByLabelText('Поиск'), 'lad');
    await userEvent.selectOptions(screen.getByLabelText('Стратегия'), strategy.id);
    await userEvent.selectOptions(screen.getByLabelText('Источник'), 'optimization');
    await userEvent.click(await screen.findByRole('radio', { name: 'RUB 3' }));
    await userEvent.click(screen.getByRole('button', { name: 'Период и результат' }));
    await userEvent.type(screen.getByLabelText('Запуск с (UTC)'), '2026-10-01');
    await userEvent.type(screen.getByLabelText('Запуск по (UTC)'), '2026-10-28');
    await userEvent.type(screen.getByLabelText('Доходность от, %'), '1.5');
    await userEvent.type(screen.getByLabelText('Просадка до, %'), 'x');

    await waitFor(() =>
      expect(lastQuery(requests, '/api/backtests')).toEqual({
        quote: 'RUB',
        strategyId: strategy.id,
        search: 'lad',
        origin: 'optimization',
        from: '2026-10-01T00:00:00.000Z',
        to: '2026-10-29T00:00:00.000Z',
        minReturn: '1.5',
        limit: '50',
        sort: 'return',
        order: 'desc',
      }),
    );
    expect(screen.getByText('Число, например 5 или -2.5.')).toBeInTheDocument();
    const address = new URLSearchParams(history.location.search);
    expect(address.get('q')).toBe('lad');
    expect(address.get('quote')).toBe('RUB');
    expect(address.get('strategy')).toBe(strategy.id);

    await userEvent.click(screen.getByRole('radio', { name: 'С ошибкой 2' }));
    await waitFor(() =>
      expect(lastQuery(requests, '/api/backtests')).toMatchObject({
        status: 'failed',
        sort: 'created',
        order: 'desc',
      }),
    );
    expect(lastQuery(requests, '/api/backtests')).not.toHaveProperty('quote');
    expect(lastQuery(requests, '/api/backtests')).not.toHaveProperty('minReturn');
    expect(screen.queryByRole('radiogroup', { name: 'Валюта котировки' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Сбросить фильтры' }));
    await waitFor(() =>
      expect(lastQuery(requests, '/api/backtests')).toEqual({
        status: 'failed',
        limit: '50',
        sort: 'created',
        order: 'desc',
      }),
    );
  });

  it('restores the filters from the address', async () => {
    const requests = fakeApi(routes());
    renderApp(`/backtests?timeframe=1h&venue=bybit&maxDrawdown=5&quote=USDT`);

    await screen.findByText('BTCUSDT', { selector: 'td' });
    expect(screen.getByLabelText('Таймфрейм')).toHaveValue('1h');
    expect(screen.getByLabelText('Площадка')).toHaveValue('bybit');
    expect(screen.getByLabelText('Просадка до, %')).toHaveValue('5');
    expect(lastQuery(requests, '/api/backtests')).toMatchObject({
      venue: 'bybit',
      timeframe: '1h',
      maxDrawdown: '5',
    });
  });

  it('lists optimizations of every strategy with their own filters', async () => {
    const requests = fakeApi(routes());
    const { history } = renderApp('/backtests');

    await userEvent.click(await screen.findByRole('tab', { name: 'Оптимизации' }));
    const table = await screen.findByRole('table');
    expect(within(table).getByText('Ladder')).toBeInTheDocument();
    expect(within(table).getByTitle('Окна walk-forward')).toHaveTextContent('WF ×6');
    expect(within(table).getByText('/ 1000')).toBeInTheDocument();
    expect(lastQuery(requests, '/api/optimizations')).toEqual({ limit: '50' });

    await userEvent.click(screen.getByRole('radio', { name: 'В работе' }));
    await userEvent.selectOptions(screen.getByLabelText('Метод'), 'tpe');
    await waitFor(() =>
      expect(lastQuery(requests, '/api/optimizations')).toEqual({
        state: 'active',
        method: 'tpe',
        limit: '50',
      }),
    );
    expect(new URLSearchParams(history.location.search).get('tab')).toBe('optimizations');
  });
});
