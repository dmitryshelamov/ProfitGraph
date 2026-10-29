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
  resistance: null,
  support: null,
  distanceToResistance: null,
  distanceToSupport: null,
  rewardRisk: null,
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
      'GET /api/scanner/bybit/levels': (request) => {
        const timeframe = new URL(request.url).searchParams.get('timeframe');
        const hourly = timeframe === '1h';

        return json({
          venue: 'bybit',
          symbol: 'SOLUSDT',
          timeframe,
          from: '2026-10-22T10:00:00Z',
          to: '2026-10-29T10:00:00Z',
          status: 'Complete',
          errorCode: null,
          candleCount: 168,
          price: '187.5',
          levels: hourly ? ['180', '200'] : ['185', '192'],
          support: hourly ? '180' : '185',
          resistance: hourly ? '200' : '192',
          distanceToSupport: hourly ? '-4' : '-1.3333',
          distanceToResistance: hourly ? '6.6667' : '2.4',
          rewardRisk: hourly ? '1.6667' : '1.8',
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
    const levels15m = (await screen.findByRole('heading', { name: 'Уровни 15m' })).closest(
      'section',
    )!;
    expect(await within(levels15m).findByText('1.8')).toBeInTheDocument();
    expect(within(levels15m).getByText('185')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('link', { name: '1h' }));

    await waitFor(() => expect(candleTimeframes()).toContain('1h'));
    const levels1h = (await screen.findByRole('heading', { name: 'Уровни 1h' })).closest(
      'section',
    )!;
    expect(await within(levels1h).findByText('1.67')).toBeInTheDocument();
    expect(within(levels1h).getByText('200')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '1h' })).toHaveAttribute('aria-current', 'page');
  });

  it('creates the ticked cards of every timeframe with the prices they show', async () => {
    const created = {
      id: 'trade-1',
      venue: 'bybit',
      symbol: 'SOLUSDT',
      scenario: 'Levels',
      entry: '100',
      stop: '95',
      target: '112',
      amount: '96.15',
      risk: '5',
      botId: 'bot-1',
      createdAt: '2026-10-30T10:00:00Z',
      expiresAt: '2026-10-30T11:00:00Z',
      status: 'Waiting',
      openedAt: null,
      closedAt: null,
      realizedPnl: null,
      failureCode: null,
    };
    let trades: object[] = [];
    const requests = fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/scanner/bybit/snapshot': () =>
        json(snapshot([{ ...emptyMetrics, symbol: 'SOLUSDT', price: '100' }])),
      'GET /api/scanner/bybit/candles': (request) => {
        const query = new URL(request.url).searchParams;

        return json({
          venue: 'bybit',
          symbol: 'SOLUSDT',
          timeframe: query.get('timeframe'),
          from: query.get('from'),
          to: query.get('to'),
          latestClosed: query.get('to'),
          status: 'Complete',
          errorCode: null,
          candles: [],
        });
      },
      'GET /api/exchange-credentials': () =>
        json([
          {
            id: 'key-1',
            venue: 'bybit',
            label: 'Основной',
            apiKeyLast4: '1234',
            publicId: null,
            createdAt: '2026-10-01T00:00:00Z',
          },
        ]),
      'GET /api/scanner/bybit/trade-settings': () =>
        json({ settings: { venue: 'bybit', credentialId: 'key-1', risk: '5', maxAmount: '1000' } }),
      'GET /api/scanner/bybit/trade-ideas': () =>
        json({
          venue: 'bybit',
          symbol: 'SOLUSDT',
          price: '100',
          hasSettings: true,
          ideas: [
            {
              timeframe: '15m',
              scenario: 'Levels',
              entry: '100',
              stop: '95',
              target: '112',
              rewardRisk: '2.4',
              entryValidityMinutes: 60,
              amount: '96.153846153846',
              risk: '5',
              taken: trades.length > 0,
            },
            {
              timeframe: '4h',
              scenario: 'Target2R',
              entry: '100',
              stop: '90',
              target: '120',
              rewardRisk: '2',
              entryValidityMinutes: 60,
              amount: '49.019607843137',
              risk: '5',
              taken: false,
            },
            {
              timeframe: '1h',
              scenario: 'Pullback',
              entry: '97.5',
              stop: '95',
              target: '102.5',
              rewardRisk: '2',
              entryValidityMinutes: 240,
              amount: '187.5',
              risk: '5',
              taken: false,
            },
          ],
        }),
      'GET /api/scanner/bybit/trades': () => json(trades),
      'POST /api/scanner/bybit/trades': () => {
        trades = [created];

        return json(created, 201);
      },
    });

    renderApp('/scanner/bybit/SOLUSDT');

    const levels = await screen.findByRole('article', { name: 'Идея: Уровни 15m' });
    expect(within(levels).getByText('96.15 USDT')).toBeInTheDocument();
    expect(within(levels).getByText('R:R 2.4')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Идеи по 4h' })).toBeInTheDocument();
    expect(screen.getByText(/Риск на сделку/)).toHaveTextContent('ключ «Основной»');

    await userEvent.click(
      within(screen.getByRole('article', { name: 'Идея: Откат 1h' })).getByRole('button', {
        name: 'Скрыть',
      }),
    );
    expect(screen.queryByRole('article', { name: 'Идея: Откат 1h' })).not.toBeInTheDocument();

    await userEvent.click(within(levels).getByRole('checkbox'));
    expect(screen.getByText(/Сумма 96.15 USDT, потеря на всех стопах 5 USDT/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Создать выбранные (1)' }));

    expect(await within(levels).findByRole('link', { name: 'бот сделки' })).toHaveAttribute(
      'href',
      '/bots/bot-1',
    );
    const posts = requests.filter((request) => request.method === 'POST');
    expect(posts).toHaveLength(1);
    expect(await posts[0]!.json()).toEqual({
      symbol: 'SOLUSDT',
      timeframe: '15m',
      scenario: 'Levels',
      entry: '100',
      stop: '95',
      target: '112',
    });
    expect(await screen.findByText('Ждёт входа')).toBeInTheDocument();
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

  it('starts a backtest of the preset being edited and shows its result', async () => {
    const outcome = (count: number, share: string | null, mean: string | null) => ({
      count,
      risingShare: share,
      mean,
      median: mean,
    });
    const completed = {
      id: 'b1',
      venue: 'bybit',
      presetName: 'Импульс (копия)',
      conditions: [{ metric: 'Change1h', minimum: '3', maximum: null }],
      hideStale: true,
      from: '2026-09-29T10:00:00Z',
      to: '2026-10-29T10:00:00Z',
      jobId: 'j1',
      createdAt: '2026-10-29T10:00:00Z',
      completedAt: '2026-10-29T10:20:00Z',
      pairs: 388,
      failedPairs: ['BADUSDT'],
      summary: {
        signals: 3,
        change15m: outcome(3, '0.6666666666666666666666666667', '1.25'),
        change1h: outcome(3, '0.3333333333333333333333333333', '-0.5'),
        change4h: outcome(2, '0.5', '0'),
        change24h: outcome(0, null, null),
        maxRise24h: outcome(0, null, null),
        maxDrop24h: outcome(0, null, null),
        scenarios: [
          {
            scenario: 'Levels',
            trades: 2,
            winningShare: '0.5',
            meanR: '0.45',
            medianR: '0.45',
            totalR: '0.9',
            meanPercent: '0.8',
            longestLosingStreak: 1,
            meanMinutes: '95',
          },
        ],
      },
      signals: [
        {
          symbol: 'HYPEUSDT',
          at: '2026-10-28T11:46:00Z',
          entryPrice: '87.439',
          metrics: { ...emptyMetrics, symbol: 'HYPEUSDT' },
          change15m: '1.2345',
          change1h: '-0.5',
          change4h: null,
          change24h: null,
          maxRise24h: null,
          maxDrop24h: null,
          trades: [
            {
              scenario: 'Levels',
              entry: '87.5',
              stop: '86',
              target: '90',
              enteredAt: '2026-10-28T11:46:00Z',
              exitedAt: '2026-10-28T13:00:00Z',
              exit: '90',
              reason: 'Target',
              resultR: '1.55',
              resultPercent: '2.65',
            },
          ],
        },
      ],
      signalsTruncated: false,
    };
    const requests = fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/scanner/venues': () => json([{ venue: 'bybit', name: 'Bybit' }]),
      'GET /api/scanner/bybit/presets': () => json([impulse]),
      'GET /api/scanner/bybit/snapshot': () => json(snapshot([])),
      'GET /api/scanner/bybit/backtests': () => json([]),
      'POST /api/scanner/bybit/backtests': () =>
        json(
          {
            ...completed,
            completedAt: null,
            summary: null,
            signals: null,
            pairs: null,
            failedPairs: null,
          },
          202,
        ),
      'GET /api/scanner/backtests/b1': () => json(completed),
    });

    renderApp('/scanner');

    // The built-in preset fills the editor once it loads.
    await screen.findByRole('textbox', { name: 'От' });
    await userEvent.click(screen.getByRole('button', { name: 'Проверить на истории (30 дней)' }));

    expect(
      await screen.findByRole('heading', { name: 'Проверка пресета «Импульс (копия)»' }),
    ).toBeInTheDocument();
    const sent = await requests.find((request) => request.method === 'POST')!.json();
    expect(sent).toMatchObject({
      name: 'Импульс (копия)',
      conditions: [{ metric: 'Change1h', minimum: '3', maximum: null }],
    });
    expect(await screen.findByText('Сигналов: 3')).toBeInTheDocument();
    const summary = screen.getAllByRole('table')[0]!;
    const quarterHour = within(summary).getByRole('row', { name: /Через 15 мин/ });
    expect(within(quarterHour).getByText('67%')).toBeInTheDocument();
    expect(within(quarterHour).getAllByText('1.25%')).toHaveLength(2);
    expect(screen.getByText(/История не загрузилась у 1: BADUSDT/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'HYPEUSDT' })).toBeInTheDocument();
    const levels = screen.getByRole('row', { name: /Уровни: стоп поддержка/ });
    expect(within(levels).getByText('0.9')).toBeInTheDocument();
    expect(within(levels).getByText('50%')).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Пара' }), 'HYPEUSDT');

    expect(
      within(screen.getByRole('list', { name: 'Итоги пары' })).getByText(/сумма 1.55 R/),
    ).toBeInTheDocument();
  });
});
