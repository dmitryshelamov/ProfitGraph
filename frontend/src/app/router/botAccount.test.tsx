import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bot, botAccount, flatPosition } from '@/test/botFixtures';
import { fakeApi, json } from '@/test/fakeApi';
import { owner } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const botPath = `/api/bots/${bot.id}`;
const user = { ...owner, role: 'User' as const, login: 'trader' };

function minutesAgo(minutes: number): string {
  return new Date(Date.now() - minutes * 60_000).toISOString();
}

function routes(overrides: Record<string, () => Response> = {}) {
  return {
    'GET /api/auth/me': () => json(user),
    [`GET ${botPath}`]: () => json(bot),
    [`GET ${botPath}/account`]: () =>
      json({ ...botAccount, snapshot: { ...botAccount.snapshot!, capturedAt: minutesAgo(3) } }),
    [`GET ${botPath}/alerts?limit=50`]: () => json([]),
    'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
    [`GET ${botPath}/fills?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/feed?limit=50`]: () => json({ items: [], next: null }),
    [`GET ${botPath}/position?cycles=20`]: () => json(flatPosition),
    'GET /api/market-data/instruments': () =>
      json([
        {
          venue: 'bybit',
          symbol: 'BTCUSDT',
          baseAsset: 'BTC',
          quoteAsset: 'USDT',
          appearance: null,
        },
      ]),
    ...overrides,
  };
}

describe('bot account summary', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('opens the bot page with the account, its value, the bot capital and the details behind a disclosure', async () => {
    const other = {
      botId: 'b2',
      name: 'ETH лесенка',
      symbol: 'ETHUSDT',
      allocationCap: '250',
      runtimeStatus: 'Running',
    };
    fakeApi(
      routes({
        [`GET ${botPath}/account`]: () =>
          json({
            ...botAccount,
            snapshot: { ...botAccount.snapshot!, capturedAt: minutesAgo(3) },
            otherRunningBots: [other],
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const card = await screen.findByRole('region', { name: 'Счёт на бирже' });
    expect(await within(card).findByText('24617703')).toBeInTheDocument();
    expect(card).toHaveTextContent(
      'UID 24617703 · «бот BTC» · основной аккаунт, чтение и торговля',
    );
    expect(card).toHaveTextContent('ключ «Основной» …abcd');
    expect(within(card).getByLabelText('Стоимость счёта')).toHaveTextContent('1234.5');
    expect(within(card).getByLabelText('Доступно на счёте')).toHaveTextContent('1000');
    expect(card).toHaveTextContent('снимок 3 мин назад');
    expect(await within(card).findByLabelText('Капитал бота')).toHaveTextContent('100 USDT');
    expect(card).toHaveTextContent('ещё запущенных ботов на ключе: 1');
    expect(within(card).queryByText(/Последнее чтение счёта не удалось/)).not.toBeInTheDocument();

    const asset = screen.getByRole('region', { name: 'Актив' });
    expect(card.compareDocumentPosition(asset) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    await userEvent.click(within(card).getByText('Подробнее'));
    expect(within(card).getByRole('list', { name: 'Монеты счёта' })).toHaveTextContent(
      'BTC: 0.0035 ≈ 234.5 USD',
    );
    expect(card).toHaveTextContent('Spot:SpotTrade, Wallet:AccountTransfer');
    expect(within(card).getByRole('link', { name: 'ETH лесенка' })).toHaveAttribute(
      'href',
      '/bots/b2',
    );
    expect(card).toHaveTextContent('ETH лесенка: капитал 250');
    expect(card.textContent).not.toContain('apiSecret');
  });

  it('says why reading failed and that the protection holds the account back', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}`]: () => json({ ...bot, venue: 'steam', symbol: 'KZT:730:Case' }),
        [`GET ${botPath}/account`]: () =>
          json({
            ...botAccount,
            venue: 'steam',
            accountPublicId: '76561190000000001',
            identity: null,
            snapshot: null,
            lastSuccessAt: null,
            failureReason: 'venue_unavailable',
            venueStatus: { status: 'Ready', nextCheckAt: null },
            protection: {
              open: true,
              reason: 'Captcha',
              since: minutesAgo(10),
              automaticResumeAt: null,
            },
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const card = await screen.findByRole('region', { name: 'Счёт на бирже' });
    expect(await within(card).findByText('76561190000000001')).toBeInTheDocument();
    expect(card).toHaveTextContent('SteamID 76561190000000001');
    expect(card).toHaveTextContent('Снимков счёта пока нет');
    expect(card).toHaveTextContent('Последнее чтение счёта не удалось: биржа не отвечает.');
    expect(card).toHaveTextContent(
      'Защита аккаунта остановила его ботов: Steam потребовал CAPTCHA.',
    );

    await userEvent.click(within(card).getByText('Подробнее'));
    expect(card).toHaveTextContent('Статус на площадке');
    expect(card).toHaveTextContent('Готов к торговле.');
  });

  it('names a T-Invest account by its number, name and kind', async () => {
    fakeApi(
      routes({
        [`GET ${botPath}`]: () => json({ ...bot, venue: 'tinvest', symbol: 'TQBR:GAZP' }),
        [`GET ${botPath}/account`]: () =>
          json({
            ...botAccount,
            venue: 'tinvest',
            accountPublicId: '2000000002',
            identity: {
              ...botAccount.identity!,
              accountId: '2000000002',
              name: 'Инвесткопилка',
              kind: 'iis',
              accessLevel: 'full_access',
              permissions: [],
              expiresAt: null,
            },
            snapshot: {
              ...botAccount.snapshot!,
              valueCurrency: 'RUB',
              availableValue: null,
              capturedAt: minutesAgo(1),
            },
          }),
      }),
    );
    renderApp(`/bots/${bot.id}`);

    const card = await screen.findByRole('region', { name: 'Счёт на бирже' });
    expect(await within(card).findByText('2000000002')).toBeInTheDocument();
    expect(card).toHaveTextContent('Счёт 2000000002 · «Инвесткопилка» · ИИС, полный доступ');
    expect(card).toHaveTextContent('Стоимость 1234.5 RUB');
    expect(within(card).queryByLabelText('Доступно на счёте')).not.toBeInTheDocument();
  });

  it('keeps the bot page working when the summary cannot be loaded', async () => {
    const withoutAccount: Record<string, () => Response> = routes();
    delete withoutAccount[`GET ${botPath}/account`];
    fakeApi(withoutAccount);
    renderApp(`/bots/${bot.id}`);

    const card = await screen.findByRole('region', { name: 'Счёт на бирже' });
    expect(await within(card).findByText('Сводка счёта недоступна.')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Актив' })).toBeInTheDocument();
  });
});
