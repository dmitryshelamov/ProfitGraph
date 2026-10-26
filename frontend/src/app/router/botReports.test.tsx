import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bot, credential, flatPosition } from '@/test/botFixtures';
import { fakeApi, json } from '@/test/fakeApi';
import { hub } from '@/test/fakeHub';
import { owner } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const botPath = `/api/bots/${bot.id}`;
const user = { ...owner, role: 'User' as const, login: 'trader' };

const buy = {
  sequence: 2,
  id: 'f2',
  engineOrderId: 1,
  side: 'Buy' as const,
  tag: null,
  grossBase: '0.001',
  grossQuote: '60',
  fee: '0.000001',
  feeAsset: 'Base' as const,
  price: '60000',
  filledAt: '2026-10-24T09:00:00Z',
  commissionPending: false,
};

const openPosition = {
  ...flatPosition,
  baseTotal: '0.000999',
  quoteTotal: '40',
  costBasis: '60',
  averageCost: '60060.06',
  openedAt: '2026-10-24T09:00:00Z',
  markPrice: '61000',
  markPriceObservedAt: '2026-10-24T10:00:00Z',
  markPriceSource: 'Close' as const,
  unrealizedPnl: '0.94',
  cycles: [{ id: 'c1', openedAt: '2026-10-24T09:00:00Z', closedAt: null, realizedPnl: null }],
};

function routes(overrides: Record<string, () => Response> = {}) {
  return {
    'GET /api/auth/me': () => json(user),
    [`GET ${botPath}`]: () => json(bot),
    [`GET ${botPath}/alerts?limit=50`]: () => json([]),
    'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
    [`GET ${botPath}/fills?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/feed?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/position?cycles=20`]: () => json(flatPosition),
    ...overrides,
  };
}

describe('bot reports', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the Steam item with its picture, kind and description', async () => {
    const symbol = 'KZT:730:Dreams & Nightmares Case';
    fakeApi(
      routes({
        [`GET ${botPath}`]: () => json({ ...bot, venue: 'steam', symbol }),
        'GET /api/market-data/instruments': () =>
          json([
            {
              venue: 'steam',
              symbol,
              baseAsset: 'Dreams & Nightmares Case',
              quoteAsset: 'KZT',
              appearance: {
                imageUrl: 'https://community.steamstatic.com/economy/image/abc/256fx256f',
                kind: 'Base Grade Container',
                nameColor: 'D2D2D2',
                backgroundColor: null,
                description: ['Contains one item'],
              },
            },
          ]),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const card = await screen.findByRole('region', { name: 'Актив' });
    expect(within(card).getByText('Dreams & Nightmares Case')).toBeInTheDocument();
    expect(await within(card).findByText('Base Grade Container')).toBeInTheDocument();
    expect(within(card).getByText('Contains one item')).toBeInTheDocument();
    expect(card.querySelector('img')).toHaveAttribute(
      'src',
      'https://community.steamstatic.com/economy/image/abc/256fx256f',
    );
  });

  it('shows only the name when the venue describes nothing', async () => {
    fakeApi(routes());
    renderApp(`/bots/${bot.id}`);

    const card = await screen.findByRole('region', { name: 'Актив' });
    expect(within(card).getByText('BTCUSDT')).toBeInTheDocument();
    expect(card.querySelector('img')).toBeNull();
  });

  it('shows the position at the stored price and switches to a newer live ticker', async () => {
    fakeApi(routes({ [`GET ${botPath}/position?cycles=20`]: () => json(openPosition) }));
    renderApp(`/bots/${bot.id}`);

    const price = await screen.findByLabelText('Цена для отображения');
    expect(price).toHaveTextContent('61000');
    expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('0.94');
    expect(screen.getByText(/закрытие последней свечи/)).toBeInTheDocument();
    expect(screen.getByText('60060.06')).toBeInTheDocument();
    expect(
      within(screen.getByRole('table', { name: 'Циклы позиции' })).getByText('открыта'),
    ).toBeInTheDocument();
    await waitFor(() => expect(hub.subscriptions).toContain(bot.id));

    hub.emit('LivePrice', {
      venue: 'bybit',
      symbol: 'BTCUSDT',
      price: '61500',
      observedAt: '2026-10-24T10:00:30Z',
    });

    await waitFor(() => expect(price).toHaveTextContent('61500'));
    expect(screen.getByText(/тикер биржи/)).toBeInTheDocument();
    // 0.000999 × 61500 − 60, exactly.
    expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('1.4385');
  });

  it('keeps the stored price when a live ticker is older', async () => {
    fakeApi(routes({ [`GET ${botPath}/position?cycles=20`]: () => json(openPosition) }));
    renderApp(`/bots/${bot.id}`);

    const price = await screen.findByLabelText('Цена для отображения');
    await waitFor(() => expect(hub.subscriptions).toContain(bot.id));
    hub.emit('LivePrice', {
      venue: 'bybit',
      symbol: 'BTCUSDT',
      price: '59000',
      observedAt: '2026-10-24T09:59:00Z',
    });

    expect(price).toHaveTextContent('61000');
    expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('0.94');
  });

  it('lists fills and loads older ones', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/fills?limit=50`]: () => json({ items: [buy], next: 2 }),
        [`GET ${botPath}/fills?limit=50&before=2`]: () =>
          json({
            items: [{ ...buy, sequence: 1, id: 'f1', side: 'Sell', price: '59000' }],
            next: null,
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const fills = await screen.findByRole('table', { name: 'Сделки бота' });
    expect(within(fills).getByText('60000')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё сделки' }));

    expect(await within(fills).findByText('59000')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Показать ещё сделки' })).not.toBeInTheDocument();
  });

  it('says when a fill’s commission has not arrived yet', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/fills?limit=50`]: () =>
          json({
            items: [
              {
                ...buy,
                id: 'f3',
                sequence: 3,
                fee: '0',
                feeAsset: 'Quote',
                commissionPending: true,
              },
              buy,
            ],
            next: null,
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const fills = await screen.findByRole('table', { name: 'Сделки бота' });
    expect(within(fills).getAllByText('комиссия ещё не пришла')).toHaveLength(1);
    expect(within(fills).getByText(/0\.000001/)).toBeInTheDocument();
  });

  it('shows working orders the venue holds locally and why', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/waiting-orders`]: () =>
          json([
            {
              engineOrderId: 3,
              code: 'PriceBand',
              since: '2026-10-24T10:00:00Z',
              until: null,
              low: '90',
              high: '110',
            },
            {
              engineOrderId: 4,
              code: 'CarryOver',
              since: '2026-10-24T20:45:00Z',
              until: null,
              low: null,
              high: null,
            },
          ]),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const waiting = await screen.findByRole('list', { name: 'Ожидающие заявки' });
    expect(
      within(waiting).getByText('Ждёт ценового коридора (коридор 90–110)'),
    ).toBeInTheDocument();
    expect(within(waiting).getByText('Перенос на следующую торговую сессию')).toBeInTheDocument();
  });

  it('lets the owner confirm an unresolved submit absent, after a second click', async () => {
    const confirmed: string[] = [];
    let confirmedAt: string | null = null;
    fakeApi(
      routes({
        [`GET ${botPath}/unresolved-orders`]: () =>
          json([
            {
              intentId: 'i1',
              engineOrderId: 1,
              side: 'Buy',
              type: 'Market',
              quantity: null,
              quoteBudget: '600',
              limitPrice: null,
              createdAt: '2026-10-26T02:35:06Z',
              placeStartedAt: '2026-10-26T02:40:43Z',
              canConfirmAbsent: confirmedAt === null,
              confirmedAbsentAt: confirmedAt,
            },
          ]),
        [`POST ${botPath}/order-intents/i1/confirm-absent`]: () => {
          confirmed.push('i1');
          confirmedAt = '2026-10-26T03:00:00Z';

          return new Response(null, { status: 204 });
        },
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const unresolved = await screen.findByRole('list', { name: 'Неясные заявки' });
    expect(within(unresolved).getByText('покупка на 600')).toBeInTheDocument();
    await userEvent.click(within(unresolved).getByRole('button', { name: 'Заявки на бирже нет' }));
    expect(confirmed).toEqual([]);
    await userEvent.click(
      within(unresolved).getByRole('button', { name: 'Подтвердить: заявки на бирже нет' }),
    );

    await waitFor(() => expect(confirmed).toEqual(['i1']));
    expect(await within(unresolved).findByText(/Отсутствие подтверждено/)).toBeInTheDocument();
  });

  it('shows the bot’s feed with readable events', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/feed?limit=50`]: () =>
          json({
            items: [
              {
                sequence: 4,
                id: 'e4',
                kind: 'FeeCorrected',
                details: { engineOrderId: '7', previousFee: '0', correctedFee: '1.5' },
                occurredAt: '2026-10-24T12:00:00Z',
              },
              {
                sequence: 3,
                id: 'e3',
                kind: 'Degraded',
                details: { reason: 'ForeignOrder' },
                occurredAt: '2026-10-24T11:00:00Z',
              },
              {
                sequence: 2,
                id: 'e2',
                kind: 'Fill',
                details: { side: 'Buy', grossBase: '0.001', grossQuote: '60' },
                occurredAt: '2026-10-24T09:00:00Z',
              },
              {
                sequence: 1,
                id: 'e1',
                kind: 'Started',
                details: {},
                occurredAt: '2026-10-24T08:00:00Z',
              },
              {
                sequence: 0,
                id: 'e0',
                kind: 'SettingsChanged',
                details: { setting: 'foreignOrders', allowForeignOrders: 'true' },
                occurredAt: '2026-10-24T07:00:00Z',
              },
            ],
            next: null,
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const feed = await screen.findByRole('list', { name: 'Лента бота' });
    expect(within(feed).getByText('Запущен')).toBeInTheDocument();
    expect(within(feed).getByText(/покупка 0.001 на 60/)).toBeInTheDocument();
    expect(within(feed).getByText(/на символе есть чужие заявки/)).toBeInTheDocument();
    expect(within(feed).getByText('Настройки изменены: общий символ включён')).toBeInTheDocument();
    expect(
      within(feed).getByText(/Комиссия уточнена: заявка 7: было 0, стало 1.5/),
    ).toBeInTheDocument();
  });
});

describe('account overview', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the latest account value of each key and why the last read failed', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json([credential]),
      [`GET /api/exchange-credentials/${credential.id}/account`]: () =>
        json({
          lastAttemptAt: '2026-10-24T10:05:00Z',
          lastSuccessAt: '2026-10-24T10:00:00Z',
          failureReason: 'venue_unavailable',
          nextAttemptAt: '2026-10-24T10:10:00Z',
          snapshots: [
            {
              capturedAt: '2026-10-24T09:55:00Z',
              totalValue: '150.5',
              valueCurrency: 'USD',
              availableValue: null,
              coins: [],
            },
            {
              capturedAt: '2026-10-24T10:00:00Z',
              totalValue: '160.25',
              valueCurrency: 'USD',
              availableValue: '100',
              coins: [{ coin: 'USDT', balance: '100', usdValue: '100' }],
            },
          ],
        }),
    });
    renderApp('/credentials');

    expect(await screen.findByText('160.25')).toBeInTheDocument();
    expect(
      within(screen.getByRole('list', { name: 'Монеты счёта' })).getByText(/USDT/),
    ).toBeInTheDocument();
    expect(screen.getByText('Снимков за неделю: 2')).toBeInTheDocument();
    expect(screen.getByText(/биржа не отвечает/)).toBeInTheDocument();
  });

  it('explains that an unused key has no snapshots yet', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json([credential]),
      [`GET /api/exchange-credentials/${credential.id}/account`]: () =>
        json({
          lastAttemptAt: null,
          lastSuccessAt: null,
          failureReason: null,
          nextAttemptAt: null,
          snapshots: [],
        }),
    });
    renderApp('/credentials');

    expect(await screen.findByText(/Снимков пока нет/)).toBeInTheDocument();
  });
});

describe('telegram profile', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('issues a link code and shows the command to send', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/profile/telegram': () =>
        json({
          enabled: true,
          linked: false,
          linkedAt: null,
          blocked: false,
          botUsername: 'profitgraph_bot',
        }),
      'POST /api/profile/telegram/link-code': () =>
        json({
          code: 'K7Q2M9',
          expiresAt: '2026-10-24T10:10:00Z',
          startLink: 'https://t.me/profitgraph_bot?start=K7Q2M9',
        }),
    });
    renderApp('/profile');

    expect(await screen.findByText('Чат не привязан.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Получить код привязки' }));

    const status = await screen.findByRole('status');
    expect(status).toHaveTextContent('@profitgraph_bot');
    expect(status).toHaveTextContent('/start K7Q2M9');
    expect(within(status).getByRole('link', { name: 'ссылку' })).toHaveAttribute(
      'href',
      'https://t.me/profitgraph_bot?start=K7Q2M9',
    );
  });

  it('unlinks a linked chat', async () => {
    let linked = true;
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/profile/telegram': () =>
        json({
          enabled: true,
          linked,
          linkedAt: linked ? '2026-10-24T08:00:00Z' : null,
          blocked: false,
          botUsername: null,
        }),
      'DELETE /api/profile/telegram': () => {
        linked = false;

        return new Response(null, { status: 204 });
      },
    });
    renderApp('/profile');

    await userEvent.click(await screen.findByRole('button', { name: 'Отвязать чат' }));

    expect(await screen.findByText('Чат не привязан.')).toBeInTheDocument();
  });

  it('opens the profile from the user’s login in the header', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/bots': () => json([]),
      'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
      'GET /api/profile/telegram': () =>
        json({ enabled: false, linked: false, linkedAt: null, blocked: false, botUsername: null }),
    });
    const { history } = renderApp('/bots');

    await userEvent.click(await screen.findByRole('link', { name: 'trader' }));

    await waitFor(() => expect(history.location.pathname).toBe('/profile'));
    expect(
      await screen.findByText('Telegram-уведомления в этой установке выключены.'),
    ).toBeInTheDocument();
  });
});
