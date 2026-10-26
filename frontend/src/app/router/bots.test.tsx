import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bot, credential, flatPosition } from '@/test/botFixtures';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { owner, strategy, summary } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const botPath = `/api/bots/${bot.id}`;
const user = { ...owner, role: 'User' as const, login: 'trader' };

function botRoutes(current = bot) {
  return {
    'GET /api/auth/me': () => json(user),
    [`GET ${botPath}`]: () => json(current),
    [`GET ${botPath}/alerts?limit=50`]: () => json([]),
    'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
    [`GET ${botPath}/fills?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/feed?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/position?cycles=20`]: () => json(flatPosition),
    [`GET ${botPath}/open-orders`]: () => json([]),
    [`GET /api/strategies/${strategy.id}`]: () => json(strategy),
  };
}

describe('bots', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists the user’s bots with their state', async () => {
    fakeApi({
      ...botRoutes(),
      'GET /api/bots': () =>
        json([
          bot,
          {
            ...bot,
            id: 'running',
            symbol: 'ETHUSDT',
            desiredState: 'Running',
            runtimeStatus: 'Degraded',
            statusReason: 'NoGateway',
          },
        ]),
    });
    renderApp('/bots');

    const table = await screen.findByRole('table');
    expect(within(table).getByRole('link', { name: 'BTCUSDT 1h' })).toBeInTheDocument();
    expect(within(table).getByText('Не запущен')).toBeInTheDocument();
    expect(
      within(table).getByText('Остановлен по безопасности: торговля в этом процессе выключена'),
    ).toBeInTheDocument();
  });

  it('creates a bot from a strategy and a key with the strategy’s default parameters', async () => {
    const created: unknown[] = [];
    fakeApi({
      ...botRoutes(),
      'GET /api/bots': () => json([]),
      'GET /api/strategies': () => json([summary]),
      [`GET /api/strategies/${strategy.id}`]: () => json(strategy),
      'GET /api/exchange-credentials': () => json([credential]),
      'POST /api/bots': async (request) => {
        created.push(await request.json());

        return json(bot, 201);
      },
    });
    const { history } = renderApp('/bots');

    await userEvent.click(await screen.findByRole('button', { name: 'Новый бот' }));
    await userEvent.selectOptions(screen.getByLabelText('Стратегия'), strategy.id);
    await userEvent.selectOptions(await screen.findByLabelText('Ключ биржи'), credential.id);
    await screen.findByLabelText('Снижение для усреднения, %');
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/bots/${bot.id}`));
    expect(created).toEqual([
      expect.objectContaining({
        strategyId: strategy.id,
        credentialId: credential.id,
        symbol: 'BTCUSDT',
        timeframe: '1h',
        allocationCap: '100',
        maxOrderQuote: '20',
        dailyLossLimitQuote: '10',
        allowForeignOrders: false,
        parameters: Object.fromEntries(
          strategy.definition.parameters.map((parameter) => [
            parameter.name,
            parameter.defaultValue,
          ]),
        ),
      }),
    ]);
  });

  it('shows the bot’s open orders with their fill', async () => {
    fakeApi({
      ...botRoutes(),
      [`GET ${botPath}/open-orders`]: () =>
        json([
          {
            engineOrderId: 2,
            tag: 'ladder.exit',
            side: 'Sell',
            type: 'Limit',
            status: 'Active',
            quantity: '832.75',
            limitPrice: '0.03872',
            quoteBudget: null,
            filledBase: '12.5',
          },
          {
            engineOrderId: 3,
            tag: 'ladder.average',
            side: 'Buy',
            type: 'Limit',
            status: 'PendingCancel',
            quantity: '893.38',
            limitPrice: '0.03358',
            quoteBudget: null,
            filledBase: '0',
          },
        ]),
    });
    renderApp(`/bots/${bot.id}`);

    const table = await screen.findByRole('table', { name: 'Активные ордера бота' });
    const exit = within(within(table).getByText('ladder.exit').closest('tr')!);
    expect(exit.getByText('Продажа')).toBeInTheDocument();
    expect(exit.getByText('0.03872')).toBeInTheDocument();
    expect(exit.getByText('12.5')).toBeInTheDocument();
    expect(exit.getByText('Стоит на бирже')).toBeInTheDocument();
    expect(within(table).getByText('Отменяется')).toBeInTheDocument();
  });

  it('shows a held-back exit as deferred with why and until when', async () => {
    fakeApi({
      ...botRoutes(),
      [`GET ${botPath}/open-orders`]: () =>
        json([
          {
            engineOrderId: 2,
            tag: 'ladder.exit',
            side: 'Sell',
            type: 'Limit',
            status: 'Active',
            quantity: '1',
            limitPrice: '692.00',
            quoteBudget: null,
            filledBase: '0',
          },
        ]),
      [`GET ${botPath}/waiting-orders`]: () =>
        json([
          {
            engineOrderId: 2,
            code: 'SettlementHold',
            since: '2026-10-26T12:00:00Z',
            until: '2026-11-02T00:00:00Z',
            low: null,
            high: null,
          },
        ]),
    });
    renderApp(`/bots/${bot.id}`);

    const table = await screen.findByRole('table', { name: 'Активные ордера бота' });
    const exit = within(within(table).getByText('ladder.exit').closest('tr')!);
    expect(await exit.findByText('Отложен')).toBeInTheDocument();
    expect(exit.getByText(/Ждёт снятия трейд-холда до/)).toBeInTheDocument();
    expect(exit.queryByText('Стоит на бирже')).not.toBeInTheDocument();
  });

  it('links the bot page to the strategy it runs', async () => {
    fakeApi(botRoutes());
    renderApp(`/bots/${bot.id}`);

    const link = await screen.findByRole('link', { name: strategy.name });
    expect(link).toHaveAttribute('href', `/strategies/${strategy.id}`);
  });

  it('says so when the bot has no open orders', async () => {
    fakeApi(botRoutes());
    renderApp(`/bots/${bot.id}`);

    expect(await screen.findByText('Активных ордеров нет.')).toBeInTheDocument();
  });

  it('starts a bot and kills it only after confirming', async () => {
    let current = bot;
    fakeApi({
      ...botRoutes(),
      [`GET ${botPath}`]: () => json(current),
      [`POST ${botPath}/start`]: () => {
        current = { ...bot, desiredState: 'Running', runtimeStatus: 'Running' };

        return json(current);
      },
      [`POST ${botPath}/kill`]: () => {
        current = {
          ...bot,
          desiredState: 'Stopped',
          runtimeStatus: 'Stopped',
          statusReason: 'Killed',
        };

        return json(current);
      },
      'GET /api/bots': () => json([current]),
    });
    renderApp(`/bots/${bot.id}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));
    expect(await screen.findByText('Работает')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Аварийная остановка' }));
    expect(screen.getByText(/позиция не продаётся/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Подтвердить аварийную остановку' }));

    expect(await screen.findByText('Остановлен: аварийная остановка')).toBeInTheDocument();
  });

  it('sends a manual order of a running bot and follows it', async () => {
    const running = { ...bot, desiredState: 'Running' as const, runtimeStatus: 'Running' as const };
    const order = {
      id: 'b1c2d3e4-f5a6-4b7c-8d9e-0f1a2b3c4d5e',
      botId: bot.id,
      side: 'Buy' as const,
      amount: '10',
      status: 'Pending' as const,
      engineOrderId: null,
      rejectionReason: null,
      createdAt: '2026-10-24T08:00:00Z',
      resolvedAt: null,
    };
    const sent: unknown[] = [];
    fakeApi({
      ...botRoutes(running),
      [`POST ${botPath}/manual-orders`]: async (request) => {
        sent.push(await request.json());

        return json(order, 202);
      },
      [`GET ${botPath}/manual-orders/${order.id}`]: () =>
        json({ ...order, status: 'Submitted', engineOrderId: 3 }),
    });
    renderApp(`/bots/${bot.id}`);

    await userEvent.type(await screen.findByLabelText('Сумма'), '10');
    await userEvent.click(screen.getByRole('button', { name: 'Отправить заявку' }));

    expect(await screen.findByText('Заявка передан в работу')).toBeInTheDocument();
    expect(sent).toEqual([{ side: 'Buy', amount: '10' }]);
  });

  it('acknowledges an alert', async () => {
    const alert = {
      id: 'a1',
      botId: bot.id,
      kind: 'Degraded' as const,
      reason: 'ForeignOrder',
      createdAt: '2026-10-24T09:00:00Z',
      acknowledgedAt: null,
    };
    let acknowledged = false;
    fakeApi({
      ...botRoutes(),
      [`GET ${botPath}/alerts?limit=50`]: () =>
        json([{ ...alert, acknowledgedAt: acknowledged ? '2026-10-24T09:05:00Z' : null }]),
      'POST /api/alerts/a1/acknowledge': () => {
        acknowledged = true;

        return json({ ...alert, acknowledgedAt: '2026-10-24T09:05:00Z' });
      },
    });
    renderApp(`/bots/${bot.id}`);

    const alerts = await screen.findByRole('list', { name: 'Алерты бота' });
    expect(
      within(alerts).getByText(/Остановлен по безопасности: на символе есть чужие заявки/),
    ).toBeInTheDocument();
    await userEvent.click(within(alerts).getByRole('button', { name: 'Прочитано' }));

    await waitFor(() =>
      expect(within(alerts).queryByRole('button', { name: 'Прочитано' })).not.toBeInTheDocument(),
    );
  });

  it('lets the Owner halt trading and shows the halt to everyone', async () => {
    let halted = false;
    fakeApi({
      ...botRoutes(),
      'GET /api/auth/me': () => json(owner),
      'GET /api/bots': () => json([]),
      'GET /api/trading-halt': () => json({ isHalted: halted, updatedAt: '2026-10-24T08:00:00Z' }),
      'POST /api/trading-halt/halt': () => {
        halted = true;

        return json({ isHalted: true, updatedAt: '2026-10-24T08:01:00Z' });
      },
    });
    renderApp('/bots');

    await userEvent.click(await screen.findByRole('button', { name: 'Остановить всю торговлю' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Торговля остановлена владельцем');
    expect(screen.getByRole('button', { name: 'Возобновить торговлю' })).toBeInTheDocument();
  });

  it('shows why a bot cannot be found', async () => {
    fakeApi({
      ...botRoutes(),
      [`GET ${botPath}`]: () => problem(404, 'bot_not_found', 'The bot does not exist.'),
    });
    renderApp(`/bots/${bot.id}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('The bot does not exist.');
  });
});

describe('exchange keys', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('adds a key and lists it by its last four characters', async () => {
    const stored: unknown[] = [];
    let keys: unknown[] = [];
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json(keys),
      'POST /api/exchange-credentials': async (request) => {
        stored.push(await request.json());
        keys = [credential];

        return json(credential, 201);
      },
    });
    renderApp('/credentials');

    await userEvent.type(await screen.findByLabelText('Название ключа'), 'Основной');
    await userEvent.type(screen.getByLabelText('API key'), 'invented-key');
    await userEvent.type(screen.getByLabelText('API secret'), 'invented-secret');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить ключ' }));

    expect(await screen.findByText('…abcd')).toBeInTheDocument();
    expect(stored).toEqual([
      { venue: 'bybit', label: 'Основной', apiKey: 'invented-key', apiSecret: 'invented-secret' },
    ]);
    expect(screen.getByLabelText('API secret')).toHaveValue('');
  });

  it('adds a T-Invest key by loading its accounts and choosing an eligible one', async () => {
    const listed: unknown[] = [];
    const stored: unknown[] = [];
    const requests = fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json([]),
      'POST /api/exchange-credentials/accounts': async (request) => {
        listed.push(await request.json());

        return json([
          {
            id: '2000000001',
            name: 'Брокерский',
            kind: 'broker',
            eligible: true,
            ineligibility: null,
          },
          {
            id: '2000000002',
            name: 'Только чтение',
            kind: 'broker',
            eligible: false,
            ineligibility: 'not_full_access',
          },
        ]);
      },
      'POST /api/exchange-credentials': async (request) => {
        stored.push(await request.json());

        return json({ ...credential, venue: 'tinvest', apiKeyLast4: '0001' }, 201);
      },
    });
    renderApp('/credentials');

    await userEvent.selectOptions(await screen.findByLabelText('Биржа'), 'tinvest');
    await userEvent.type(screen.getByLabelText('Название ключа'), 'Брокер');
    const save = screen.getByRole('button', { name: 'Сохранить ключ' });
    expect(save).toBeDisabled();
    await userEvent.type(screen.getByLabelText('Токен T-Invest'), 't.invented-token');
    await userEvent.click(screen.getByRole('button', { name: 'Загрузить счета' }));

    expect(await screen.findByRole('radio', { name: /Брокерский/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /Только чтение/ })).toBeDisabled();
    expect(screen.getByText(/у токена нет полного доступа/)).toBeInTheDocument();
    await userEvent.click(save);

    await waitFor(() => expect(stored).toHaveLength(1));
    expect(listed).toEqual([{ venue: 'tinvest', fields: { token: 't.invented-token' } }]);
    expect(stored).toEqual([
      {
        venue: 'tinvest',
        label: 'Брокер',
        apiKey: null,
        apiSecret: null,
        fields: { token: 't.invented-token', accountId: '2000000001' },
      },
    ]);
    expect(screen.getByLabelText('Токен T-Invest')).toHaveValue('');
    expect(requests.some((request) => request.url.includes('t.invented-token'))).toBe(false);
  });

  it('forgets the loaded accounts when the token changes', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json([]),
      'POST /api/exchange-credentials/accounts': () =>
        json([
          {
            id: '2000000001',
            name: 'Брокерский',
            kind: 'broker',
            eligible: true,
            ineligibility: null,
          },
        ]),
    });
    renderApp('/credentials');

    await userEvent.selectOptions(await screen.findByLabelText('Биржа'), 'tinvest');
    await userEvent.type(screen.getByLabelText('Токен T-Invest'), 't.first');
    await userEvent.click(screen.getByRole('button', { name: 'Загрузить счета' }));
    expect(await screen.findByRole('radio', { name: /Брокерский/ })).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Токен T-Invest'), '-changed');

    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Сохранить ключ' })).toBeDisabled();
  });
});
