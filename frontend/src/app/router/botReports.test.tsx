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

  it('shows the bot’s feed with readable events', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/feed?limit=50`]: () =>
          json({
            items: [
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
