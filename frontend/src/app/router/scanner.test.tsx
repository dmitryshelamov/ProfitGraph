import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { hub } from '@/test/fakeHub';
import { owner } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const user = { ...owner, role: 'User' as const, login: 'trader' };

const emptyMetrics = {
  price: null,
  change5m: null,
  change15m: null,
  change1h: null,
  change4h: null,
  change24h: null,
  turnover1h: null,
  turnover24h: null,
  volumeSpike: null,
  natr24h: null,
  efficiencyRatio24h: null,
  distanceToHigh24h: null,
  distanceToLow24h: null,
  distanceToHigh7d: null,
  distanceToLow7d: null,
  spread: null,
  listingAgeDays: null,
  stale: false,
  incomplete: false,
};

const impulse = {
  id: null,
  venue: 'bybit',
  name: 'Импульс',
  conditions: [{ metric: 'Change1h', minimum: '3', maximum: null }],
  sortMetric: 'Change1h',
  sortDescending: true,
  hideStale: true,
  builtIn: true,
  alertsEnabled: false,
};

function snapshot(rows: object[]) {
  return { venue: 'bybit', computedAt: '2026-10-29T10:01:05Z', rows };
}

describe('scanner page', () => {
  beforeEach(() => hub.reset());

  it('shows the pairs the built-in preset keeps and reads a new snapshot when the hub announces one', async () => {
    let current = snapshot([
      { ...emptyMetrics, symbol: 'SOLUSDT', change1h: '4.25', turnover24h: '1234567.8' },
      { ...emptyMetrics, symbol: 'BTCUSDT', change1h: '0.5' },
      { ...emptyMetrics, symbol: 'PEPEUSDT', change1h: '12', stale: true },
    ]);
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/scanner/venues': () => json([{ venue: 'bybit', name: 'Bybit' }]),
      'GET /api/scanner/bybit/presets': () => json([impulse]),
      'GET /api/scanner/bybit/snapshot': () => json(current),
    });

    renderApp('/scanner');

    const table = await screen.findByRole('table');
    expect(within(table).getByText('SOLUSDT')).toBeInTheDocument();
    expect(within(table).queryByText('BTCUSDT')).not.toBeInTheDocument();
    expect(within(table).queryByText('PEPEUSDT')).not.toBeInTheDocument();
    // The default text matcher folds the thin spaces that group thousands into plain ones.
    expect(within(table).getByText('1 234 568')).toBeInTheDocument();
    expect(screen.getByText(/Показано 1 из 3 пар/)).toBeInTheDocument();

    current = snapshot([{ ...emptyMetrics, symbol: 'ETHUSDT', change1h: '3' }]);
    hub.emit('ScanSnapshotChanged', undefined);

    expect(await within(table).findByText('ETHUSDT')).toBeInTheDocument();
  });

  it('saves a copy of the built-in preset as the user own one', async () => {
    const requests = fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/scanner/venues': () => json([{ venue: 'bybit', name: 'Bybit' }]),
      'GET /api/scanner/bybit/presets': () => json([impulse]),
      'GET /api/scanner/bybit/snapshot': () => json(snapshot([])),
      'POST /api/scanner/bybit/presets': async (request) =>
        json({ ...(await request.json()), id: 'p1', venue: 'bybit', builtIn: false }, 201),
    });

    renderApp('/scanner');

    const minimum = await screen.findByRole('textbox', { name: 'От' });
    await userEvent.clear(minimum);
    await userEvent.type(minimum, '5');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить пресет' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'POST')).toBe(true));
    const sent = await requests.find((request) => request.method === 'POST')!.json();
    expect(sent).toEqual({
      name: 'Импульс (копия)',
      conditions: [{ metric: 'Change1h', minimum: '5', maximum: null }],
      sortMetric: 'Change1h',
      sortDescending: true,
      hideStale: true,
    });
    expect(await screen.findByRole('button', { name: 'Сохранить как новый' })).toBeInTheDocument();
  });

  it('opens a pair chart from its row and switches the timeframe', async () => {
    const requests = fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/scanner/venues': () => json([{ venue: 'bybit', name: 'Bybit' }]),
      'GET /api/scanner/bybit/presets': () => json([impulse]),
      'GET /api/scanner/bybit/snapshot': () =>
        json(snapshot([{ ...emptyMetrics, symbol: 'SOLUSDT', change1h: '4.25', price: '187.5' }])),
      'GET /api/scanner/bybit/candles': (request) => {
        const query = new URL(request.url).searchParams;

        return json({
          venue: 'bybit',
          symbol: query.get('symbol'),
          timeframe: query.get('timeframe'),
          from: query.get('from'),
          to: query.get('to'),
          latestClosed: query.get('to'),
          status: 'Complete',
          errorCode: null,
          candles: [],
        });
      },
    });
    const candleTimeframes = () =>
      requests
        .filter((request) => new URL(request.url).pathname === '/api/scanner/bybit/candles')
        .map((request) => new URL(request.url).searchParams.get('timeframe'));

    renderApp('/scanner');

    await userEvent.click(await screen.findByRole('link', { name: 'SOLUSDT' }));

    expect(await screen.findByRole('heading', { name: 'SOLUSDT' })).toBeInTheDocument();
    expect(await screen.findByText('187.5')).toBeInTheDocument();
    await waitFor(() => expect(candleTimeframes()).toContain('15m'));

    await userEvent.click(screen.getByRole('link', { name: '1h' }));

    await waitFor(() => expect(candleTimeframes()).toContain('1h'));
    expect(screen.getByRole('link', { name: '1h' })).toHaveAttribute('aria-current', 'page');
  });

  it('switches on the alerts of an own preset and warns without a linked chat', async () => {
    const own = { ...impulse, id: 'p1', name: 'Мой', builtIn: false };
    let alertsEnabled = false;
    const requests = fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/scanner/venues': () => json([{ venue: 'bybit', name: 'Bybit' }]),
      'GET /api/scanner/bybit/presets': () => json([{ ...own, alertsEnabled }]),
      'GET /api/scanner/bybit/snapshot': () => json(snapshot([])),
      'GET /api/profile/telegram': () =>
        json({
          enabled: true,
          linked: false,
          linkedAt: null,
          blocked: false,
          botUsername: 'pg_bot',
        }),
      'PUT /api/scanner/presets/p1/alerts': async (request) => {
        alertsEnabled = ((await request.json()) as { enabled: boolean }).enabled;

        return json({ ...own, alertsEnabled });
      },
    });

    renderApp('/scanner');

    const toggle = await screen.findByRole('checkbox', { name: 'Алерты в Telegram' });
    expect(toggle).not.toBeChecked();
    await userEvent.click(toggle);

    await waitFor(() =>
      expect(
        requests.some((request) => request.url.endsWith('/api/scanner/presets/p1/alerts')),
      ).toBe(true),
    );
    expect(await screen.findByRole('checkbox', { name: 'Алерты в Telegram' })).toBeChecked();
    expect(await screen.findByText(/Чат Telegram не привязан/)).toBeInTheDocument();
  });
});
