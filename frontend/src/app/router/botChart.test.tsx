import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { components } from '@/shared/api/generated/schema';
import { bot, flatPosition } from '@/test/botFixtures';
import { fakeApi, json } from '@/test/fakeApi';
import { owner } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

type Schemas = components['schemas'];

const botPath = `/api/bots/${bot.id}`;
const user = { ...owner, role: 'User' as const, login: 'trader' };
const hour = 3_600_000;
const latest = Math.floor(Date.now() / hour) * hour;

function at(offsetHours: number): string {
  return new Date(latest + offsetHours * hour).toISOString();
}

function candle(offsetHours: number, close: string): Schemas['CandleResponse'] {
  return { openTime: at(offsetHours), open: close, high: close, low: close, close, volume: '1' };
}

/** The venue's hourly history: three recent candles and one far back, before the first page. */
const history = [candle(-600, '50'), candle(-3, '100'), candle(-2, '101'), candle(-1, '102')];

const fill: Schemas['BotChartFillResponse'] = {
  id: 'f1',
  filledAt: new Date(latest - 2 * hour + 15 * 60_000).toISOString(),
  side: 'Buy',
  price: '100.5',
  grossBase: '0.001',
  tag: null,
};

/** Answers each page from the history, cut at the latest closed candle as the server does. */
function candles(overrides: Partial<Schemas['BotCandlesResponse']> = {}) {
  return (request: Request) => {
    const url = new URL(request.url);
    const from = Date.parse(url.searchParams.get('from') ?? '');
    const to = Math.min(Date.parse(url.searchParams.get('to') ?? ''), latest);
    const inRange = (time: string) => Date.parse(time) >= from && Date.parse(time) < to;

    return json({
      venue: bot.venue,
      symbol: bot.symbol,
      timeframe: bot.timeframe,
      from: new Date(from).toISOString(),
      to: new Date(Math.max(from, to)).toISOString(),
      latestClosed: new Date(latest).toISOString(),
      status: 'Complete',
      errorCode: null,
      candles: history.filter((item) => inRange(item.openTime)),
      fills: [fill].filter((item) => inRange(item.filledAt)),
      fillsTruncated: false,
      ...overrides,
    } satisfies Schemas['BotCandlesResponse']);
  };
}

function routes(overrides: Record<string, (request: Request) => Response> = {}) {
  return {
    'GET /api/auth/me': () => json(user),
    [`GET ${botPath}`]: () => json(bot),
    [`GET ${botPath}/alerts?limit=50`]: () => json([]),
    'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
    [`GET ${botPath}/fills?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/feed?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/position?cycles=20`]: () => json(flatPosition),
    [`GET ${botPath}/open-orders`]: () =>
      json([
        {
          engineOrderId: 7,
          tag: null,
          side: 'Sell',
          type: 'Limit',
          status: 'Open',
          quantity: '0.001',
          limitPrice: '105',
          quoteBudget: null,
          filledBase: '0',
        },
      ]),
    [`GET ${botPath}/candles`]: candles(),
    ...overrides,
  };
}

async function chart(): Promise<HTMLElement> {
  const figure = await screen.findByRole('figure', { name: 'Свечи бота' });
  await waitFor(() => expect(figure).toHaveAttribute('data-candles', '3'));

  return figure;
}

describe('bot chart', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the closed candles of the bot’s timeframe with its fills and working orders', async () => {
    const requests = fakeApi(routes());
    renderApp(`/bots/${bot.id}`);

    const figure = await chart();
    expect(figure).toHaveAttribute('data-markers', '1');
    await waitFor(() => expect(figure).toHaveAttribute('data-levels', '1'));
    const card = figure.closest('section') as HTMLElement;
    expect(within(card).getByRole('link', { name: '1h · бот' })).toHaveAttribute(
      'aria-current',
      'page',
    );

    const first = requests.find((request) => new URL(request.url).pathname.endsWith('/candles'));
    const query = new URL(first!.url).searchParams;
    expect(Date.parse(query.get('to')!) - Date.parse(query.get('from')!)).toBe(500 * hour);
  });

  it('loads earlier candles once and stops where the history begins', async () => {
    fakeApi(routes());
    renderApp(`/bots/${bot.id}`);
    const figure = await chart();
    const earlier = screen.getByRole('button', { name: 'Загрузить раньше' });

    await userEvent.click(earlier);
    await waitFor(() => expect(figure).toHaveAttribute('data-candles', '4'));
    await userEvent.click(earlier);

    expect(await screen.findByText('Более ранней истории нет.')).toBeInTheDocument();
    expect(figure).toHaveAttribute('data-candles', '4');
    expect(earlier).toBeDisabled();
  });

  it('says that intervals without candles are left out, as Steam hours without sales', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}`]: () =>
          json({ ...bot, venue: 'steam', symbol: 'KZT:730:Dreams & Nightmares Case' }),
        [`GET ${botPath}/candles`]: candles({ status: 'Partial' }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    await chart();
    expect(
      await screen.findByText(/есть интервалы без свечей, например часы или дни без продаж/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Не удалось загрузить свечи/)).not.toBeInTheDocument();
  });

  it('shows the stored candles when the venue does not answer', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/candles`]: candles({
          status: 'Unavailable',
          errorCode: 'provider_unavailable',
        }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    await chart();
    expect(
      await screen.findByText('Показаны только сохранённые свечи: площадка сейчас не отвечает.'),
    ).toBeInTheDocument();
  });

  it('switches to another timeframe of the venue with the fills on its candles and no forming candle', async () => {
    const fourHours = 4 * hour;
    const latestFour = Math.floor(latest / fourHours) * fourHours;
    const requests = fakeApi(
      routes({
        'GET /api/market-data/venues': () =>
          json([
            {
              id: bot.venue,
              name: 'Bybit',
              timeframes: ['1m', '1h', '4h', '1d'],
              executionDefaults: null,
              botSettings: null,
              backtestLimits: [],
            },
          ]),
        [`GET ${botPath}/candles`]: (request) => {
          const timeframe = new URL(request.url).searchParams.get('timeframe');

          if (timeframe !== '4h') {
            return candles()(request);
          }

          return json({
            venue: bot.venue,
            symbol: bot.symbol,
            timeframe: '4h',
            from: new Date(latestFour - 500 * fourHours).toISOString(),
            to: new Date(latestFour).toISOString(),
            latestClosed: new Date(latestFour).toISOString(),
            status: 'Complete',
            errorCode: null,
            candles: [-3, -2, -1].map((offset) => ({
              openTime: new Date(latestFour + offset * fourHours).toISOString(),
              open: '100',
              high: '103',
              low: '99',
              close: '101',
              volume: '1',
            })),
            fills: [{ ...fill, filledAt: new Date(latestFour - fourHours + 60_000).toISOString() }],
            fillsTruncated: false,
          } satisfies Schemas['BotCandlesResponse']);
        },
      }),
    );
    const { history: address } = renderApp(`/bots/${bot.id}`);
    await chart();
    const group = await screen.findByRole('group', { name: 'Таймфрейм' });
    await waitFor(() =>
      expect(
        within(group)
          .getAllByRole('link')
          .map((link) => link.textContent),
      ).toEqual(['1m', '1h · бот', '4h', '1d']),
    );

    await userEvent.click(within(group).getByRole('link', { name: '4h' }));

    expect(address.location.search).toContain('timeframe=4h');
    const figure = await screen.findByRole('figure', { name: 'Свечи бота' });
    await waitFor(() => expect(figure).toHaveAttribute('data-candles', '3'));
    expect(figure).toHaveAttribute('data-markers', '1');
    expect(figure).toHaveAttribute('data-live', '0');
    expect(within(group).getByRole('link', { name: '4h' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByText(/На 4h показаны только закрытые свечи/)).toBeInTheDocument();
    expect(
      requests.some((request) => new URL(request.url).searchParams.get('timeframe') === '4h'),
    ).toBe(true);
  });

  it('opens on the bot’s own timeframe when the address names one the venue does not offer', async () => {
    const requests = fakeApi(routes());
    renderApp(`/bots/${bot.id}?timeframe=3m`);

    await chart();
    const asked = requests
      .filter((request) => new URL(request.url).pathname.endsWith('/candles'))
      .map((request) => new URL(request.url).searchParams.get('timeframe'));
    expect(asked.every((timeframe) => timeframe === '1h')).toBe(true);
  });

  it('draws a level trade’s entry, target and stop and explains that the stop exits at market on a candle close', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}`]: () =>
          json({
            ...bot,
            parameters: {
              buyAmount: '50',
              entryPrice: '100',
              stopPrice: '95',
              targetPrice: '105',
            },
          }),
        [`GET ${botPath}/position?cycles=20`]: () =>
          json({
            ...flatPosition,
            baseTotal: '0.5',
            quoteTotal: '50',
            costBasis: '50',
            averageCost: '100',
            averageBuyPrice: '100',
            openedAt: at(-2),
            markPrice: '102',
            markPriceObservedAt: new Date().toISOString(),
            markPriceSource: 'Ticker',
            unrealizedPnl: '1',
          } satisfies Schemas['BotPositionResponse']),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const figure = await chart();
    // The working sale at the target is folded into the target's line.
    await waitFor(() => expect(figure).toHaveAttribute('data-levels', '3'));
    expect(
      screen.getByText(
        'Стоп 95 — не заявка на бирже: продажа по рынку, когда свеча 1h закрылась на уровне стопа или ниже.',
      ),
    ).toBeInTheDocument();

    const plan = await screen.findByRole('region', { name: /План сделки/ });
    expect(within(plan).getByText(/лонг на 50/)).toBeInTheDocument();
    expect(within(plan).getByLabelText('Стоп сделки')).toHaveTextContent('95');
    expect(within(plan).getByText('−6.86% от текущей · −5% от средней')).toBeInTheDocument();
    expect(within(plan).getByText('+2.94% от текущей · +5% от средней')).toBeInTheDocument();
    expect(within(plan).getByText(/не заявка на бирже: продажа по рынку/)).toBeInTheDocument();
    expect(plan).toHaveTextContent('На стопе ≈ −2.5');
  });

  it('shows no trade plan for a bot of another strategy', async () => {
    fakeApi(routes());
    renderApp(`/bots/${bot.id}`);

    const figure = await chart();
    await waitFor(() => expect(figure).toHaveAttribute('data-levels', '1'));
    await screen.findByText('Позиции нет');
    expect(screen.queryByRole('region', { name: /План сделки/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/не заявка на бирже/)).not.toBeInTheDocument();
  });
});
