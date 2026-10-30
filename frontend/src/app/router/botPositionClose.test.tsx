import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { components } from '@/shared/api/generated/schema';
import { bot, flatPosition } from '@/test/botFixtures';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { owner } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

type Schemas = components['schemas'];

const botPath = `/api/bots/${bot.id}`;
const user = { ...owner, role: 'User' as const, login: 'trader' };
const running = { ...bot, desiredState: 'Running' as const, runtimeStatus: 'Running' as const };

const openPosition: Schemas['BotPositionResponse'] = {
  ...flatPosition,
  baseTotal: '0.0999',
  quoteTotal: '90',
  costBasis: '10',
  entryFees: '0.01',
  averageCost: '100.1001',
  averageBuyPrice: '100',
  realizedPnl: '-0.01',
  openedAt: '2026-10-30T00:00:00Z',
  markPrice: '104',
  markPriceObservedAt: '2026-10-30T00:10:00Z',
  markPriceSource: 'Candle',
  unrealizedPnl: '0.3996',
  positionFees: '0.01',
};

const exit: Schemas['BotOpenOrderResponse'] = {
  engineOrderId: 2,
  tag: 'ladder.exit',
  side: 'Sell',
  type: 'Limit',
  status: 'Active',
  quantity: '0.0999',
  limitPrice: '104.4',
  quoteBudget: null,
  filledBase: '0',
};

function close(
  overrides: Partial<Schemas['BotPositionCloseResponse']>,
): Schemas['BotPositionCloseResponse'] {
  return {
    id: 'c1',
    botId: bot.id,
    stopAfter: false,
    status: 'Pending',
    cancelledOrders: null,
    engineOrderId: null,
    failureReason: null,
    createdAt: new Date().toISOString(),
    resolvedAt: null,
    ...overrides,
  };
}

function routes(latest: () => Response) {
  return {
    'GET /api/auth/me': () => json(user),
    [`GET ${botPath}`]: () => json(running),
    [`GET ${botPath}/alerts?limit=50`]: () => json([]),
    'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
    [`GET ${botPath}/position?cycles=20`]: () => json(openPosition),
    [`GET ${botPath}/open-orders`]: () => json([exit]),
    [`GET ${botPath}/position-close`]: latest,
  };
}

describe('position close', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('confirms what is sold and cancelled, then follows the close to its end', async () => {
    let state: Schemas['BotPositionCloseResponse'] | null = null;
    const requests = fakeApi({
      ...routes(() => (state ? json(state) : problem(404, 'position_close_not_found'))),
      [`POST ${botPath}/position-close`]: () => {
        state = close({ status: 'Pending' });

        return json(state, 202);
      },
    });
    renderApp(`/bots/${bot.id}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Закрыть позицию' }));
    const confirmation = screen.getByRole('region', { name: 'Подтверждение закрытия позиции' });
    expect(within(confirmation).getByText('0.0999')).toBeInTheDocument();
    expect(
      within(confirmation).getByText('Сначала будут отменены ордера бота: 1'),
    ).toBeInTheDocument();
    expect(within(confirmation).getByText(/Продажа 104\.4 × 0\.0999/)).toBeInTheDocument();
    expect(within(confirmation).getByRole('radio', { name: /Остановить бота/ })).toBeChecked();
    await userEvent.click(within(confirmation).getByRole('radio', { name: /Бот работает дальше/ }));
    await userEvent.click(within(confirmation).getByRole('button', { name: 'Закрыть по рынку' }));

    const progress = await screen.findByRole('region', { name: 'Закрытие позиции' });
    expect(within(progress).getByText('Бот принимает запрос')).toBeInTheDocument();
    const posted = requests.find((request) => request.method === 'POST');
    expect(await posted!.json()).toEqual({ stopAfter: false });

    state = close({
      status: 'Closed',
      cancelledOrders: 1,
      engineOrderId: 3,
      resolvedAt: new Date().toISOString(),
    });

    expect(
      await screen.findByText('Позиция закрыта, бот работает дальше.', {}, { timeout: 3000 }),
    ).toBeInTheDocument();
  });

  it('says why a close failed', async () => {
    fakeApi(
      routes(() =>
        json(
          close({
            status: 'Failed',
            stopAfter: true,
            cancelledOrders: 1,
            failureReason: 'InsufficientBase',
            resolvedAt: new Date().toISOString(),
          }),
        ),
      ),
    );
    renderApp(`/bots/${bot.id}`);

    expect(
      await screen.findByText('Закрыть позицию не удалось: не хватает актива. Бот остановлен.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Закрыть позицию' })).not.toBeInTheDocument();
  });

  it('offers no close for a bot that does not run', async () => {
    fakeApi({
      ...routes(() => problem(404, 'position_close_not_found')),
      [`GET ${botPath}`]: () => json(bot),
    });
    renderApp(`/bots/${bot.id}`);

    await screen.findByLabelText('Нереализованная прибыль');
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Закрыть позицию' })).not.toBeInTheDocument(),
    );
  });
});
