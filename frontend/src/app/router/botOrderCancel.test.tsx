import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { Bot } from '@/modules/bots';
import { bot, flatPosition } from '@/test/botFixtures';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { owner, strategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const botPath = `/api/bots/${bot.id}`;
const user = { ...owner, role: 'User' as const, login: 'trader' };
const running = { ...bot, desiredState: 'Running' as const, runtimeStatus: 'Running' as const };
const stopped = { ...bot, desiredState: 'Stopped' as const, runtimeStatus: 'Stopped' as const };
const stopping = { ...bot, desiredState: 'Stopped' as const, runtimeStatus: 'Running' as const };
const exit = {
  engineOrderId: 2,
  tag: 'ladder.exit',
  side: 'Sell' as const,
  type: 'Limit' as const,
  status: 'Active' as const,
  quantity: '0.1',
  limitPrice: '110.5',
  quoteBudget: null,
  filledBase: '0',
};
const average = {
  ...exit,
  engineOrderId: 3,
  tag: 'ladder.average',
  side: 'Buy' as const,
  limitPrice: '95',
};
const cancel = {
  id: 'd1c2d3e4-f5a6-4b7c-8d9e-0f1a2b3c4d5e',
  botId: bot.id,
  engineOrderId: 2,
  status: 'Pending' as const,
  rejectionReason: null,
  createdAt: '2026-10-28T08:00:00Z',
  resolvedAt: null,
};

function routes(current: Bot = running, openOrders: () => unknown = () => [exit, average]) {
  return {
    'GET /api/auth/me': () => json(user),
    [`GET ${botPath}`]: () => json(current),
    [`GET ${botPath}/alerts?limit=50`]: () => json([]),
    'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
    [`GET ${botPath}/fills?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/feed?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/position?cycles=20`]: () => json(flatPosition),
    [`GET ${botPath}/open-orders`]: () => json(openOrders()),
    [`GET /api/strategies/${strategy.id}`]: () => json(strategy),
  };
}

/** The first render loads the bot page lazily, which can take longer than a query's default second. */
async function exitRow() {
  const table = await screen.findByRole(
    'table',
    { name: 'Активные ордера бота' },
    { timeout: 5_000 },
  );

  return within(within(table).getByText('ladder.exit').closest('tr')!);
}

describe('owner cancel of an open order', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('cancels an order after the confirmation and follows it until the order is gone', async () => {
    let posted = false;
    let answered = false;
    const requests = fakeApi({
      ...routes(running, () =>
        answered ? [average] : [posted ? { ...exit, status: 'PendingCancel' } : exit, average],
      ),
      [`POST ${botPath}/open-orders/2/cancel`]: () => {
        posted = true;

        return json(cancel, 202);
      },
      [`GET ${botPath}/order-cancels/${cancel.id}`]: () => {
        answered = true;

        return json({ ...cancel, status: 'Requested', resolvedAt: '2026-10-28T08:00:01Z' });
      },
    });
    const sent = () => requests.filter((request) => request.method === 'POST');
    renderApp(`/bots/${bot.id}`);

    await userEvent.click((await exitRow()).getByRole('button', { name: 'Отменить ордер 2' }));

    expect(screen.getByText(/Отменить ордер 2 \(ladder\.exit\): продажа 0\.1/)).toBeInTheDocument();
    expect(
      screen.getByText(/стратегия выставит недостающие ордера заново по своим правилам/),
    ).toBeInTheDocument();
    expect(sent()).toEqual([]);
    await userEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }));

    expect(await screen.findByText(/Ордер 2 больше не активен/)).toBeInTheDocument();
    expect(sent().map((request) => new URL(request.url).pathname)).toEqual([
      `${botPath}/open-orders/2/cancel`,
    ]);
    expect(screen.queryByText('ladder.exit')).not.toBeInTheDocument();
  });

  it('shows the pending cancel on its row while the bot works on it', async () => {
    let posted = false;
    fakeApi({
      ...routes(running, () => [posted ? { ...exit, status: 'PendingCancel' } : exit, average]),
      [`POST ${botPath}/open-orders/2/cancel`]: () => {
        posted = true;

        return json(cancel, 202);
      },
      [`GET ${botPath}/order-cancels/${cancel.id}`]: () =>
        json({ ...cancel, status: 'Requested', resolvedAt: '2026-10-28T08:00:01Z' }),
    });
    renderApp(`/bots/${bot.id}`);

    await userEvent.click((await exitRow()).getByRole('button', { name: 'Отменить ордер 2' }));
    await userEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }));

    expect(
      await screen.findByText('Биржу попросили отменить ордер 2, ждём подтверждения…'),
    ).toBeInTheDocument();
    expect((await exitRow()).getByText('Отмена…')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Отменить ордер 3' })).toBeDisabled();
  });

  it('shows why the bot rejected the cancel', async () => {
    fakeApi({
      ...routes(),
      [`POST ${botPath}/open-orders/2/cancel`]: () => json(cancel, 202),
      [`GET ${botPath}/order-cancels/${cancel.id}`]: () =>
        json({
          ...cancel,
          status: 'Rejected',
          rejectionReason: 'order_not_open',
          resolvedAt: '2026-10-28T08:00:01Z',
        }),
    });
    renderApp(`/bots/${bot.id}`);

    await userEvent.click((await exitRow()).getByRole('button', { name: 'Отменить ордер 2' }));
    await userEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }));

    expect(
      await screen.findByText('Отмена ордера 2 отклонена: ордер уже не активен.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Отменить ордер 2' })).toBeEnabled();
  });

  it('explains a refused cancel', async () => {
    fakeApi({
      ...routes(),
      [`POST ${botPath}/open-orders/2/cancel`]: () => problem(409, 'order_cancel_request_pending'),
    });
    renderApp(`/bots/${bot.id}`);

    await userEvent.click((await exitRow()).getByRole('button', { name: 'Отменить ордер 2' }));
    await userEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }));

    expect(
      await screen.findByText('Не удалось отменить ордер: предыдущая отмена ещё ждёт бота.'),
    ).toBeInTheDocument();
  });

  it('goes back from the confirmation without sending', async () => {
    const requests = fakeApi(routes());
    renderApp(`/bots/${bot.id}`);

    await userEvent.click((await exitRow()).getByRole('button', { name: 'Отменить ордер 2' }));
    await userEvent.click(screen.getByRole('button', { name: 'Назад' }));

    expect(screen.queryByText(/Отменить ордер 2 \(ladder\.exit\)/)).not.toBeInTheDocument();
    expect(requests.filter((request) => request.method === 'POST')).toEqual([]);
  });

  it('offers no cancel while the bot is still stopping', async () => {
    fakeApi(routes(stopping));
    renderApp(`/bots/${bot.id}`);

    await exitRow();
    expect(screen.queryByRole('button', { name: /Отменить ордер/ })).not.toBeInTheDocument();
    expect(
      screen.getByText('Отменить ордер можно, пока бот работает или после его остановки.'),
    ).toBeInTheDocument();
  });

  it('cancels an order of a stopped bot at once', async () => {
    let posted = false;
    const requests = fakeApi({
      ...routes(stopped, () => (posted ? [average] : [exit, average])),
      [`POST ${botPath}/open-orders/2/cancel`]: () => {
        posted = true;

        return json({ ...cancel, status: 'Requested', resolvedAt: '2026-10-28T08:00:00Z' }, 202);
      },
      [`GET ${botPath}/order-cancels/${cancel.id}`]: () =>
        json({ ...cancel, status: 'Requested', resolvedAt: '2026-10-28T08:00:00Z' }),
    });
    renderApp(`/bots/${bot.id}`);

    await userEvent.click((await exitRow()).getByRole('button', { name: 'Отменить ордер 2' }));

    expect(
      screen.getByText(/Бот остановлен: биржу попросят снять ордер сразу/),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/При следующем запуске стратегия выставит недостающие ордера заново/),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }));

    expect(await screen.findByText(/Ордер 2 больше не активен/)).toBeInTheDocument();
    expect(
      requests
        .filter((request) => request.method === 'POST')
        .map((request) => new URL(request.url).pathname),
    ).toEqual([`${botPath}/open-orders/2/cancel`]);
    expect(screen.queryByText('ladder.exit')).not.toBeInTheDocument();
  });

  it('explains that a stopped bot’s unsent order waits for the start', async () => {
    fakeApi({
      ...routes(stopped),
      [`POST ${botPath}/open-orders/2/cancel`]: () => problem(409, 'order_not_sent'),
    });
    renderApp(`/bots/${bot.id}`);

    await userEvent.click((await exitRow()).getByRole('button', { name: 'Отменить ордер 2' }));
    await userEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }));

    expect(
      await screen.findByText(
        'Не удалось отменить ордер: ордер ещё не отправлен на биржу и ждёт запуска бота.',
      ),
    ).toBeInTheDocument();
  });

  it('offers no cancel for an order already being cancelled', async () => {
    fakeApi(routes(running, () => [{ ...exit, status: 'PendingCancel' }, average]));
    renderApp(`/bots/${bot.id}`);

    await exitRow();
    expect(screen.queryByRole('button', { name: 'Отменить ордер 2' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Отменить ордер 3' })).toBeInTheDocument();
  });

  it('records the owner’s cancel in the feed', async () => {
    fakeApi({
      ...routes(),
      [`GET ${botPath}/feed?limit=50`]: () =>
        json({
          items: [
            {
              sequence: 1,
              id: 'e1c2d3e4-f5a6-4b7c-8d9e-0f1a2b3c4d5e',
              kind: 'OrderCancelRequested',
              details: { engineOrderId: '2' },
              occurredAt: '2026-10-28T08:00:01Z',
            },
          ],
          next: null,
        }),
    });
    renderApp(`/bots/${bot.id}?tab=feed`);

    const feed = await screen.findByRole('list', { name: 'Лента бота' });
    expect(
      within(feed).getByText(/Отмена ордера по запросу владельца: заявка 2/),
    ).toBeInTheDocument();
  });
});
