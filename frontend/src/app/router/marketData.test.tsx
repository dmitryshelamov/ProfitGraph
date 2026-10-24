import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { owner } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const venues = [
  { id: 'bybit', name: 'Bybit', timeframes: ['1m', '1h', '1d'] },
  { id: 'other', name: 'Другая', timeframes: ['1d'] },
];

function job(status: string, extra: Record<string, unknown> = {}) {
  return {
    id: 'job-1',
    kind: 'historical_download',
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

describe('historical data', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('queues a download of a picked pair and follows the job to its result', async () => {
    const bodies: unknown[] = [];
    let polls = 0;
    const requests = fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/market-data/venues': () => json(venues),
      'GET /api/market-data/instruments': () =>
        json([{ venue: 'bybit', symbol: 'ETHUSDT', baseAsset: 'ETH', quoteAsset: 'USDT' }]),
      'POST /api/historical-data/downloads': async (request) => {
        bodies.push(await request.json());

        return json(job('Queued'), 202);
      },
      'GET /api/jobs/job-1': () =>
        json(
          ++polls === 1
            ? job('Running', { completed: 3, total: 10 })
            : job('Completed', {
                completed: 10,
                total: 10,
                result: { total: 10, cached: 4, downloaded: 6 },
              }),
        ),
    });
    renderApp('/historical-data');

    const form = await screen.findByRole('form', { name: 'Загрузка истории' });
    await waitFor(() => expect(within(form).getByLabelText('Площадка')).toHaveValue('bybit'));
    const pair = within(form).getByLabelText('Пара');
    await userEvent.clear(pair);
    await userEvent.type(pair, 'eth');
    await waitFor(() =>
      expect(form.querySelector('datalist option[value="ETHUSDT"]')).not.toBeNull(),
    );
    await userEvent.clear(pair);
    await userEvent.type(pair, 'ETHUSDT');
    await userEvent.selectOptions(within(form).getByLabelText('Таймфрейм'), '1d');
    await userEvent.type(
      within(form).getByLabelText('С (UTC), пусто — с начала истории'),
      '2026-01-01',
    );
    await userEvent.click(within(form).getByRole('button', { name: 'Загрузить историю' }));

    expect(await screen.findByText('Выполняется')).toBeInTheDocument();
    expect(screen.getByText('3 из 10')).toBeInTheDocument();
    expect(await screen.findByText(/загружено: 6/, {}, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.getByText('Завершена')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        venue: 'bybit',
        symbol: 'ETHUSDT',
        timeframe: '1d',
        from: '2026-01-01T00:00:00.000Z',
        to: null,
      },
    ]);
    expect(
      requests.some(
        (request) => request.url.includes('venue=bybit') && request.url.includes('query=eth'),
      ),
    ).toBe(true);
  });

  it('cancels a queued download', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/market-data/venues': () => json(venues),
      'GET /api/market-data/instruments': () => json([]),
      'POST /api/historical-data/downloads': () => json(job('Queued'), 202),
      'GET /api/jobs/job-1': () => json(job('Queued')),
      'POST /api/jobs/job-1/cancel': () =>
        json(job('Cancelled', { finishedAt: '2026-10-24T10:00:01Z' }), 202),
    });
    renderApp('/historical-data');

    const form = await screen.findByRole('form', { name: 'Загрузка истории' });
    await waitFor(() => expect(within(form).getByLabelText('Площадка')).toHaveValue('bybit'));
    await userEvent.click(within(form).getByRole('button', { name: 'Загрузить историю' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Отменить' }));

    expect(await screen.findByText('Отменена')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Отменить' })).not.toBeInTheDocument();
  });

  it('shows the API’s reasons when a download cannot be queued', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/market-data/venues': () => json(venues),
      'GET /api/market-data/instruments': () => json([]),
      'POST /api/historical-data/downloads': () =>
        new Response(
          JSON.stringify({
            status: 400,
            code: 'validation_failed',
            errors: [
              { code: 'invalid_symbol', field: 'Symbol', message: 'Пара не поддерживается.' },
            ],
          }),
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
    });
    renderApp('/historical-data');

    const form = await screen.findByRole('form', { name: 'Загрузка истории' });
    await waitFor(() => expect(within(form).getByLabelText('Площадка')).toHaveValue('bybit'));
    await userEvent.click(within(form).getByRole('button', { name: 'Загрузить историю' }));

    expect(await within(form).findByText('Пара не поддерживается.')).toBeInTheDocument();
  });

  // jsdom's FormData cannot be sent through Node's Request, so the upload itself is covered end to end (e2e/history.spec.ts).
  it('lets the Owner pick a CSV file to import', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/market-data/venues': () => json(venues),
      'GET /api/market-data/instruments': () => json([]),
    });
    renderApp('/historical-data');

    const form = await screen.findByRole('form', { name: 'Импорт CSV' });
    await waitFor(() => expect(within(form).getByLabelText('Площадка')).toHaveValue('bybit'));
    const importButton = within(form).getByRole('button', { name: 'Импортировать' });
    expect(importButton).toBeDisabled();

    await userEvent.upload(
      within(form).getByLabelText('Файл CSV'),
      new File(['openTime,open,high,low,close,volume\n'], 'btc.csv', { type: 'text/csv' }),
    );

    expect(importButton).toBeEnabled();
  });

  it('hides CSV import from other users', async () => {
    fakeApi({
      'GET /api/auth/me': () => json({ ...owner, login: 'user', role: 'User' }),
      'GET /api/market-data/venues': () => json(venues),
      'GET /api/market-data/instruments': () => json([]),
    });
    renderApp('/historical-data');

    expect(await screen.findByRole('form', { name: 'Загрузка истории' })).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Импорт CSV' })).not.toBeInTheDocument();
  });

  it('reports venues that cannot be loaded', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/market-data/venues': () => problem(500, 'unexpected_error'),
    });
    renderApp('/historical-data');

    expect(
      (await screen.findAllByText('Не удалось загрузить список площадок.', {}, { timeout: 5000 }))
        .length,
    ).toBeGreaterThan(0);
  });
});
