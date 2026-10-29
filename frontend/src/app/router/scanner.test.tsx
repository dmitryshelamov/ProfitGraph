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
});
