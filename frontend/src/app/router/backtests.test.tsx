import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { queuedRun } from '@/test/backtestFixtures';
import blockCatalog from '@/test/blockCatalog.fixture.json';
import { fakeApi, json } from '@/test/fakeApi';
import { owner, strategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const strategyPath = `/api/strategies/${strategy.id}`;
const venues = [{ id: 'bybit', name: 'Bybit', timeframes: ['1h', '4h', '1d'] }];
const queued = {
  id: 'run-1',
  kind: 'backtest',
  status: 'Queued',
  completed: 0,
  total: null,
  cancelRequested: false,
  createdAt: '2026-10-24T10:00:00Z',
  startedAt: null,
  finishedAt: null,
  result: null,
  failure: null,
};

function routes(start: (request: Request) => Response | Promise<Response>) {
  return {
    'GET /api/auth/me': () => json(owner),
    [`GET ${strategyPath}`]: () => json(strategy),
    [`GET ${strategyPath}/versions`]: () =>
      json([{ id: 'v2', number: 2, createdAt: '2026-10-24T09:30:00Z' }]),
    [`POST ${strategyPath}/validate?version=2`]: () =>
      json({ isValid: true, version: 2, diagnostics: [] }),
    'GET /api/market-data/venues': () => json(venues),
    'GET /api/market-data/instruments': () => json([]),
    'POST /api/backtests': start,
    'GET /api/jobs/run-1': () => json(queued),
    'GET /api/backtests/run-1': () => json({ ...queuedRun, id: 'run-1' }),
    'GET /api/backtests/run-1/observations?after=0&limit=100': () =>
      json({ items: [], next: null }),
  };
}

describe('backtest setup', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('starts a run of the current version from the strategy page with exact settings', async () => {
    const bodies: unknown[] = [];
    fakeApi(
      routes(async (request) => {
        bodies.push(await request.json());

        return json(queued, 202);
      }),
    );
    const { history } = renderApp(`/strategies/${strategy.id}`);

    await userEvent.click(await screen.findByRole('link', { name: 'Запустить прогон' }));
    await waitFor(() =>
      expect(history.location.pathname).toBe(`/strategies/${strategy.id}/backtest`),
    );
    const form = await screen.findByRole('form', { name: 'Настройка прогона' });
    await waitFor(() => expect(within(form).getByLabelText('Площадка')).toHaveValue('bybit'));
    expect(within(form).getByLabelText('Снижение для усреднения, %')).toHaveValue('1.5');
    await userEvent.selectOptions(within(form).getByLabelText('Таймфрейм'), '4h');
    await userEvent.type(
      within(form).getByLabelText('С (UTC), пусто — с начала истории'),
      '2026-01-01',
    );
    await userEvent.type(
      within(form).getByLabelText('По (UTC), пусто — до последней закрытой свечи'),
      '2026-03-01',
    );
    const capital = within(form).getByLabelText('Начальный капитал, в валюте котировки');
    await userEvent.clear(capital);
    await userEvent.type(capital, '2500.50');
    const drop = within(form).getByLabelText('Снижение для усреднения, %');
    await userEvent.clear(drop);
    await userEvent.type(drop, '2');
    await userEvent.click(within(form).getByRole('button', { name: 'Запустить прогон' }));

    await waitFor(() => expect(history.location.pathname).toBe('/backtests/run-1'));
    expect(await screen.findByText('В очереди')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        strategyId: strategy.id,
        version: 2,
        venue: 'bybit',
        symbol: 'BTCUSDT',
        timeframe: '4h',
        from: '2026-01-01T00:00:00.000Z',
        to: '2026-03-01T00:00:00.000Z',
        parameters: { buyAmount: '25', drop: '0.02' },
        startingCapital: '2500.50',
        slippage: '0',
        limitFeeRate: '0.001',
        marketFeeRate: '0.001',
        outOfSampleFraction: '0.2',
      },
    ]);
  });

  it('explains invalid settings without starting a run', async () => {
    const requests = fakeApi(routes(() => json(queued, 202)));
    renderApp(`/strategies/${strategy.id}/backtest`);

    const form = await screen.findByRole('form', { name: 'Настройка прогона' });
    await waitFor(() => expect(within(form).getByLabelText('Площадка')).toHaveValue('bybit'));
    const capital = within(form).getByLabelText('Начальный капитал, в валюте котировки');
    await userEvent.clear(capital);
    await userEvent.type(capital, '0');
    const holdout = within(form).getByLabelText('Отложенная выборка в конце периода, %');
    await userEvent.clear(holdout);
    await userEvent.type(holdout, '95');
    await userEvent.click(within(form).getByRole('button', { name: 'Запустить прогон' }));

    expect(await within(form).findByText('Укажите положительную сумму.')).toBeInTheDocument();
    expect(within(form).getByText('От 0 до 90%.')).toBeInTheDocument();
    expect(capital).toHaveAttribute('aria-invalid', 'true');
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });

  it('shows the API’s reasons when a run cannot start', async () => {
    fakeApi(
      routes(
        () =>
          new Response(
            JSON.stringify({
              status: 400,
              code: 'validation_failed',
              errors: [
                {
                  code: 'invalid_split',
                  field: 'OutOfSampleFraction',
                  message: 'Отложенная выборка пуста.',
                },
              ],
            }),
            { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
          ),
      ),
    );
    renderApp(`/strategies/${strategy.id}/backtest`);

    const form = await screen.findByRole('form', { name: 'Настройка прогона' });
    await waitFor(() => expect(within(form).getByLabelText('Площадка')).toHaveValue('bybit'));
    await userEvent.click(within(form).getByRole('button', { name: 'Запустить прогон' }));

    expect(await within(form).findByText('Отложенная выборка пуста.')).toBeInTheDocument();
  });

  it('opens the setup from a saved editor', async () => {
    fakeApi({
      ...routes(() => json(queued, 202)),
      'GET /api/block-catalog': () => json(blockCatalog),
    });
    const { history } = renderApp(`/strategies/${strategy.id}/edit`);

    await userEvent.click(await screen.findByRole('link', { name: 'Прогон' }));

    await waitFor(() =>
      expect(history.location.pathname).toBe(`/strategies/${strategy.id}/backtest`),
    );
  });
});
