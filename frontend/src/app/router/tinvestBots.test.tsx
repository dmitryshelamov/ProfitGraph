import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bot, credential } from '@/test/botFixtures';
import { fakeApi, json } from '@/test/fakeApi';
import { owner, strategy, summary } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const user = { ...owner, role: 'User' as const, login: 'trader' };
const tinvestKey = {
  ...credential,
  id: 'tinvest-key',
  label: 'T-Invest',
  venue: 'tinvest',
  apiKeyLast4: '0001',
};

describe('T-Invest bots', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('creates a bot on a share found with the chosen key, on the venue’s own timeframes', async () => {
    const created: unknown[] = [];
    const searched: string[] = [];
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/bots': () => json([]),
      'GET /api/strategies': () => json([summary]),
      [`GET /api/strategies/${strategy.id}`]: () => json(strategy),
      'GET /api/exchange-credentials': () => json([tinvestKey]),
      'GET /api/market-data/venues': () =>
        json([
          {
            id: 'tinvest',
            name: 'T-Invest (MOEX)',
            timeframes: ['1m', '1h', '1d'],
            executionDefaults: null,
            botSettings: null,
          },
        ]),
      'GET /api/market-data/instruments': (request) => {
        searched.push(new URL(request.url).searchParams.get('credentialId') ?? '');

        return json([
          { venue: 'tinvest', symbol: 'TQBR:SBER', baseAsset: 'SBER', quoteAsset: 'RUB' },
        ]);
      },
      'POST /api/bots': async (request) => {
        created.push(await request.json());

        return json({ ...bot, venue: 'tinvest', symbol: 'TQBR:SBER' }, 201);
      },
      [`GET /api/bots/${bot.id}`]: () => json({ ...bot, venue: 'tinvest', symbol: 'TQBR:SBER' }),
    });
    renderApp('/bots');

    await userEvent.click(await screen.findByRole('button', { name: 'Новый бот' }));
    await userEvent.selectOptions(screen.getByLabelText('Стратегия'), strategy.id);
    await userEvent.selectOptions(await screen.findByLabelText('Ключ биржи'), tinvestKey.id);
    const symbol = screen.getByLabelText('Символ');
    expect(symbol).toHaveValue('');
    expect(symbol).toHaveAttribute('placeholder', 'TQBR:SBER');
    await userEvent.type(symbol, 'tqbr:sber');
    await waitFor(() => expect(searched).toContain(tinvestKey.id));
    await waitFor(() =>
      expect(document.querySelector('#bot-symbol-suggestions option')).toHaveAttribute(
        'value',
        'TQBR:SBER',
      ),
    );
    expect(
      Array.from((screen.getByLabelText('Таймфрейм') as HTMLSelectElement).options).map(
        (option) => option.value,
      ),
    ).toEqual(['1m', '1h', '1d']);
    await userEvent.selectOptions(screen.getByLabelText('Таймфрейм'), '1d');
    await screen.findByLabelText('Снижение для усреднения, %');
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    await waitFor(() => expect(created).toHaveLength(1));
    expect(created[0]).toMatchObject({
      credentialId: tinvestKey.id,
      symbol: 'TQBR:SBER',
      timeframe: '1d',
    });
  });
});
