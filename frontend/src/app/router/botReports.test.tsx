import { fireEvent, screen, waitFor, within } from '@testing-library/react';
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
  averageBuyPrice: '60060.06',
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

  it('shows a T-Invest share with its logo on the brand color, name and facts', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}`]: () => json({ ...bot, venue: 'tinvest', symbol: 'TQBR:GAZP' }),
        'GET /api/market-data/instruments': () =>
          json([
            {
              venue: 'tinvest',
              symbol: 'TQBR:GAZP',
              baseAsset: 'GAZP',
              quoteAsset: 'RUB',
              appearance: {
                imageUrl: 'https://invest-brands.cdn-tinkoff.ru/RU0007661625x160.png',
                kind: 'Газпром',
                nameColor: null,
                backgroundColor: '0A2E8C',
                description: ['Сектор: energy'],
              },
            },
          ]),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const card = await screen.findByRole('region', { name: 'Актив' });
    expect(within(card).getByRole('heading', { name: 'GAZP' })).toBeInTheDocument();
    expect(await within(card).findByText('Газпром')).toBeInTheDocument();
    expect(within(card).getByText('Сектор: energy')).toBeInTheDocument();
    expect(card.querySelector('img')).toHaveAttribute(
      'src',
      'https://invest-brands.cdn-tinkoff.ru/RU0007661625x160.png',
    );
  });

  it('shows a Bybit coin icon and falls back to the letters when it does not load', async () => {
    fakeApi(
      routes({
        'GET /api/market-data/instruments': () =>
          json([
            {
              venue: 'bybit',
              symbol: 'BTCUSDT',
              baseAsset: 'BTC',
              quoteAsset: 'USDT',
              appearance: {
                imageUrl: 'https://assets.coincap.io/assets/icons/btc@2x.png',
                kind: null,
                nameColor: null,
                backgroundColor: null,
                description: [],
              },
            },
          ]),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const card = await screen.findByRole('region', { name: 'Актив' });
    const image = await waitFor(() => {
      const found = card.querySelector('img');
      expect(found).toHaveAttribute('src', 'https://assets.coincap.io/assets/icons/btc@2x.png');

      return found as HTMLImageElement;
    });

    fireEvent.error(image);

    await waitFor(() => expect(card.querySelector('img')).toBeNull());
    expect(within(card).getByText('BTCU')).toBeInTheDocument();
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
    expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('+0.94');
    expect(screen.getByText(/закрытие последней свечи/)).toBeInTheDocument();
    expect(screen.getByText('60060.06')).toBeInTheDocument();
    expect(screen.getByLabelText('Реализованная прибыль')).toHaveTextContent('—');
    expect(screen.getByText('продаж ещё не было')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Сделки' }));
    expect(
      within(await screen.findByRole('table', { name: 'Циклы позиции' })).getByText('открыт'),
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

  it('shows realized profit once a cycle closed', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/position?cycles=20`]: () =>
          json({
            ...openPosition,
            realizedPnl: '-1.5',
            cycles: [
              ...openPosition.cycles,
              {
                id: 'c0',
                openedAt: '2026-10-23T09:00:00Z',
                closedAt: '2026-10-23T12:00:00Z',
                realizedPnl: '-1.5',
              },
            ],
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    expect(await screen.findByLabelText('Реализованная прибыль')).toHaveTextContent('−1.5');
    await userEvent.click(screen.getByRole('tab', { name: 'Сделки' }));
    expect(
      within(await screen.findByRole('table', { name: 'Циклы позиции' })).getByText('−1.5'),
    ).toBeInTheDocument();
  });

  it('shows a remainder below the quantity step as no position, exactly and without rounding it up', async () => {
    fakeApi(
      routes({
        'GET /api/market-data/instrument-rules': () =>
          json({
            quantityStep: '0.01',
            priceTick: '0.0001',
            minQuantity: '0.1',
            minNotional: '1',
            basePrecision: 2,
            quotePrecision: 6,
          }),
        'GET /api/market-data/instruments': () =>
          json([
            {
              venue: 'bybit',
              symbol: 'BTCUSDT',
              baseAsset: 'TRX',
              quoteAsset: 'USDT',
              appearance: null,
            },
          ]),
        [`GET ${botPath}/position?cycles=20`]: () =>
          json({
            ...flatPosition,
            baseTotal: '0.00574',
            quoteTotal: '100.049353',
            costBasis: '0.002045',
            averageCost: '0.3563',
            realizedPnl: '0.049353',
            markPrice: '0.356',
            markPriceObservedAt: '2026-10-24T10:00:00Z',
            markPriceSource: 'Close',
            unrealizedPnl: '0.00000044',
            cycles: [
              {
                id: 'c1',
                openedAt: '2026-10-24T03:00:00Z',
                closedAt: '2026-10-24T04:08:00Z',
                realizedPnl: '0.049353',
              },
            ],
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    expect(
      await screen.findByText(
        'Позиции нет · остаток 0.00574 TRX — меньше шага 0.01 TRX, продать нельзя',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('Позиция открыта')).not.toBeInTheDocument();
    const volume = screen.getByText('Объём').closest('div') as HTMLElement;
    expect(within(volume).getByText('0.00574 TRX')).toBeInTheDocument();
    expect(screen.queryByText(/0\.01 TRX$/)).not.toBeInTheDocument();
    expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('—');
    expect(screen.getByLabelText('Реализованная прибыль')).toHaveTextContent('+0.049353 USDT');
  });

  it('reports a buy’s fee as spent and keeps it in the break-even price', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/position?cycles=20`]: () =>
          json({
            ...openPosition,
            baseTotal: '0.0999',
            costBasis: '10',
            entryFees: '0.01',
            averageCost: '100.1001',
            averageBuyPrice: '100',
            realizedPnl: '-0.01',
            markPrice: '104',
            unrealizedPnl: '0.3996',
            positionFees: '0.01',
            cycles: [{ ...openPosition.cycles[0], fees: '0.01' }],
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    expect(await screen.findByLabelText('Средняя цена покупки')).toHaveTextContent('100');
    expect(screen.getByText('безубыток 100.1001')).toBeInTheDocument();
    expect(screen.getByLabelText('Реализованная прибыль')).toHaveTextContent('−0.01');
    expect(screen.getByText('с комиссиями покупок')).toBeInTheDocument();
    expect(screen.getByLabelText('Комиссии позиции')).toHaveTextContent('0.01');
    expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('+0.3996');
    await waitFor(() => expect(hub.subscriptions).toContain(bot.id));

    hub.emit('LivePrice', {
      venue: 'bybit',
      symbol: 'BTCUSDT',
      price: '105',
      observedAt: '2026-10-24T10:00:30Z',
    });

    // 0.0999 × 105 − (10 − 0.01): the buy's fee is already in the realized profit.
    await waitFor(() =>
      expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('+0.4995'),
    );
    await userEvent.click(screen.getByRole('tab', { name: 'Сделки' }));
    const cycles = await screen.findByRole('table', { name: 'Циклы позиции' });
    expect(within(cycles).getByRole('columnheader', { name: 'Комиссии' })).toBeInTheDocument();
    expect(within(cycles).getByText('0.01')).toBeInTheDocument();
  });

  it('shows a short with the price it sold at, its proceeds owed back and its profit as the price falls', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/position?cycles=20`]: () =>
          json({
            ...openPosition,
            baseTotal: '-10',
            quoteTotal: '1999',
            costBasis: '999',
            entryFees: '1',
            averageCost: '99.9',
            averageBuyPrice: '100',
            realizedPnl: '-1',
            markPrice: '90',
            unrealizedPnl: '100',
            positionFees: '1',
            cycles: [{ ...openPosition.cycles[0], fees: '1' }],
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    expect(await screen.findByText('Шорт открыт')).toBeInTheDocument();
    expect(screen.getByText('продано на 1000')).toBeInTheDocument();
    expect(screen.getByLabelText('Средняя цена продажи')).toHaveTextContent('100');
    expect(screen.getByText('безубыток 99.9')).toBeInTheDocument();
    expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('+100');
    expect(screen.getByText('+10% к цене продажи')).toBeInTheDocument();
    expect(screen.getByLabelText('Реализованная прибыль')).toHaveTextContent('−1');
    expect(screen.getByText('с комиссиями продаж')).toBeInTheDocument();
    expect(screen.getByLabelText('Комиссии позиции')).toHaveTextContent('1');
    // 1999 − 1000: what the short sold for is owed back.
    expect(screen.getByText('999')).toBeInTheDocument();
    expect(screen.getByText('без выручки шорта')).toBeInTheDocument();
    await waitFor(() => expect(hub.subscriptions).toContain(bot.id));

    hub.emit('LivePrice', {
      venue: 'bybit',
      symbol: 'BTCUSDT',
      price: '95',
      observedAt: '2026-10-24T10:00:30Z',
    });

    // −10 × 95 + (999 + 1): the sell's fee is already in the realized profit.
    await waitFor(() =>
      expect(screen.getByLabelText('Нереализованная прибыль')).toHaveTextContent('+50'),
    );
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
    renderApp(`/bots/${bot.id}?tab=fills`);

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
    renderApp(`/bots/${bot.id}?tab=fills`);

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
              acceptedByVenue: false,
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

  it('words the confirmation of an accepted order the venue removed, and refuses one with an execution', async () => {
    const confirmed: string[] = [];
    fakeApi(
      routes({
        [`GET ${botPath}/unresolved-orders`]: () =>
          json([
            {
              intentId: 'i2',
              engineOrderId: 2,
              side: 'Buy',
              type: 'Limit',
              quantity: '10',
              quoteBudget: null,
              limitPrice: '99',
              createdAt: '2026-10-26T07:00:00Z',
              placeStartedAt: '2026-10-26T07:00:01Z',
              canConfirmAbsent: true,
              confirmedAbsentAt: null,
              acceptedByVenue: true,
            },
            {
              intentId: 'i3',
              engineOrderId: 3,
              side: 'Sell',
              type: 'Limit',
              quantity: '5',
              quoteBudget: null,
              limitPrice: '101',
              createdAt: '2026-10-26T07:00:00Z',
              placeStartedAt: '2026-10-26T07:00:01Z',
              canConfirmAbsent: false,
              confirmedAbsentAt: null,
              acceptedByVenue: true,
            },
          ]),
        [`POST ${botPath}/order-intents/i2/confirm-absent`]: () => {
          confirmed.push('i2');

          return new Response(null, { status: 204 });
        },
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const unresolved = await screen.findByRole('list', { name: 'Неясные заявки' });
    expect(
      within(unresolved).getByText(/Бот проверяет такие заявки сам, каждые 5 минут/),
    ).toBeInTheDocument();
    expect(
      within(unresolved).getByText(/Подтвердить нельзя: по заявке уже есть исполнение/),
    ).toBeInTheDocument();
    await userEvent.click(within(unresolved).getByRole('button', { name: 'Заявка снята биржей' }));
    expect(
      within(unresolved).getByText(/в приложении брокера заявки нет среди активных/),
    ).toBeInTheDocument();
    expect(confirmed).toEqual([]);
    await userEvent.click(
      within(unresolved).getByRole('button', {
        name: 'Подтвердить: заявка снята, исполнения не было',
      }),
    );

    await waitFor(() => expect(confirmed).toEqual(['i2']));
  });

  it('shows the bot’s feed with readable events', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}/feed?limit=50`]: () =>
          json({
            items: [
              {
                sequence: 6,
                id: 'e6',
                kind: 'OrderAbsenceProven',
                details: {
                  engineOrderId: '9',
                  checks: 'NotFoundByRequestId,TradingDayEnded,PositionMatchesEngine',
                },
                occurredAt: '2026-10-24T13:00:00Z',
              },
              {
                sequence: 5,
                id: 'e5',
                kind: 'Degraded',
                details: { reason: 'UnresolvedOrder', detail: 'AutoCheck:PositionDiffers' },
                occurredAt: '2026-10-24T12:30:00Z',
              },
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
                details: {
                  side: 'Buy',
                  grossBase: '0.001',
                  grossQuote: '60',
                  fee: '0.000001',
                  feeAsset: 'Base',
                },
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
              {
                sequence: -1,
                id: 'e-1',
                kind: 'SettingsChanged',
                details: { setting: 'parameters', added: 'buyAmount=50', removed: 'rise' },
                occurredAt: '2026-10-24T06:00:00Z',
              },
            ],
            next: null,
          }),
      }),
    );
    renderApp(`/bots/${bot.id}?tab=feed`);

    const feed = await screen.findByRole('list', { name: 'Лента бота' });
    expect(within(feed).getByText('Запущен')).toBeInTheDocument();
    expect(
      within(feed).getByText(/покупка 0\.001 на 60 · комиссия 0\.06 .* \(0\.000001 /),
    ).toBeInTheDocument();
    expect(within(feed).getByText(/на символе есть чужие заявки/)).toBeInTheDocument();
    expect(within(feed).getByText('Настройки изменены: общий символ включён')).toBeInTheDocument();
    expect(
      within(feed).getByText(
        'Настройки изменены: параметры стратегии: по умолчанию buyAmount=50; удалены rise',
      ),
    ).toBeInTheDocument();
    expect(
      within(feed).getByText(/Комиссия уточнена: заявка 7: было 0, стало 1.5/),
    ).toBeInTheDocument();
    expect(
      within(feed).getByText(
        'Заявка закрыта автоматически: заявка 9 закрыта автоматически, потому что биржа не находит заявку по номеру запроса; торговый день заявки закончился; позиция по бумаге равна ожидаемой ботом, ничего не заблокировано',
      ),
    ).toBeInTheDocument();
    expect(
      within(feed).getByText(
        /состояние заявки на бирже не установлено; автоматически заявку закрыть не удалось: позиция по бумаге не равна ожидаемой ботом/,
      ),
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
