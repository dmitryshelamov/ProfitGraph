import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bot, credential, flatPosition } from '@/test/botFixtures';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { owner, strategy, summary } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const user = { ...owner, role: 'User' as const, login: 'trader' };
const botPath = `/api/bots/${bot.id}`;

describe('key column', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the visible part on the first line and a mask on the second', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () =>
        json([
          { ...credential, publicId: 'fake-public-key' },
          { ...credential, id: 'old', label: 'Старый', publicId: null, apiKeyLast4: '8604' },
        ]),
    });
    renderApp('/credentials');

    const rows = (await screen.findAllByRole('row')).slice(1);
    const first = within(rows[0]!.querySelectorAll('td')[2] as HTMLElement);
    expect(first.getByText('fake-public-key')).toBeInTheDocument();
    expect(first.getByText('***')).toBeInTheDocument();
    expect(screen.queryByText('…abcd')).not.toBeInTheDocument();
    const old = within(rows[1]!.querySelectorAll('td')[2] as HTMLElement);
    expect(old.getByText('…8604')).toBeInTheDocument();
    expect(old.getByText('***')).toBeInTheDocument();
  });
});

describe('renaming', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renames a key from the list', async () => {
    const sent: unknown[] = [];
    let label = credential.label;
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json([{ ...credential, label }]),
      [`PUT /api/exchange-credentials/${credential.id}/label`]: async (request) => {
        const body = (await request.json()) as { label: string };
        sent.push(body);
        label = body.label;

        return json({ ...credential, label });
      },
    });
    renderApp('/credentials');

    await userEvent.click(await screen.findByRole('button', { name: `Переименовать «${label}»` }));
    const field = screen.getByLabelText(`Название ключа ${label}`);
    await userEvent.clear(field);
    await userEvent.type(field, 'Рабочий');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByText('Рабочий')).toBeInTheDocument();
    expect(sent).toEqual([{ label: 'Рабочий' }]);
    expect(screen.queryByRole('button', { name: 'Сохранить' })).not.toBeInTheDocument();
  });

  it('does not save a blank key name and shows the API’s refusal', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json([credential]),
      [`PUT /api/exchange-credentials/${credential.id}/label`]: () =>
        problem(400, 'invalid_credential_label', 'Слишком длинное название.'),
    });
    renderApp('/credentials');

    await userEvent.click(await screen.findByRole('button', { name: /Переименовать «/ }));
    const field = screen.getByLabelText(`Название ключа ${credential.label}`);
    await userEvent.clear(field);
    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeDisabled();
    await userEvent.type(field, 'Новое');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByText('Слишком длинное название.')).toBeInTheDocument();
  });

  it('renames a strategy from the list', async () => {
    const sent: unknown[] = [];
    let name = summary.name;
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/strategies': () => json([{ ...summary, name }]),
      [`PUT /api/strategies/${summary.id}/name`]: async (request) => {
        const body = (await request.json()) as { name: string };
        sent.push(body);
        name = body.name;

        return json({ ...strategy, name });
      },
    });
    renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: `Переименовать «${name}»` }));
    const field = screen.getByRole('textbox', { name: /Название стратегии/ });
    await userEvent.clear(field);
    await userEvent.type(field, 'Лесенка 2');
    await userEvent.keyboard('{Enter}');

    expect(await screen.findByRole('link', { name: 'Лесенка 2' })).toBeInTheDocument();
    expect(sent).toEqual([{ name: 'Лесенка 2' }]);
  });

  it('names a bot, falls back to symbol and timeframe when the name is cleared', async () => {
    const sent: unknown[] = [];
    let current: typeof bot = bot;
    fakeApi({
      'GET /api/auth/me': () => json(user),
      [`GET ${botPath}`]: () => json(current),
      [`GET ${botPath}/alerts?limit=50`]: () => json([]),
      'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
      [`GET ${botPath}/fills?limit=50`]: () => json({ items: [], next: null }),
      [`GET ${botPath}/feed?limit=50`]: () => json({ items: [], next: null }),
      [`GET ${botPath}/position?cycles=20`]: () => json(flatPosition),
      [`GET ${botPath}/open-orders`]: () => json([]),
      [`GET /api/strategies/${strategy.id}`]: () => json(strategy),
      [`PUT ${botPath}/name`]: async (request) => {
        const body = (await request.json()) as { name: string };
        sent.push(body);
        current = { ...bot, name: body.name === '' ? null : body.name };

        return json(current);
      },
    });
    renderApp(`/bots/${bot.id}`);

    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('BTCUSDT 1h');
    await userEvent.click(screen.getByRole('button', { name: 'Переименовать' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Название бота' }), 'Основной бот');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() =>
      expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Основной бот'),
    );

    await userEvent.click(screen.getByRole('button', { name: 'Переименовать' }));
    await userEvent.clear(screen.getByRole('textbox', { name: 'Название бота' }));
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() =>
      expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('BTCUSDT 1h'),
    );
    expect(sent).toEqual([{ name: 'Основной бот' }, { name: '' }]);
  });

  it('shows a bot’s own name in the list', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/bots': () => json([{ ...bot, name: 'Основной бот' }]),
      'GET /api/trading-halt': () => json({ isHalted: false, updatedAt: '2026-10-24T08:00:00Z' }),
    });
    renderApp('/bots');

    expect(await screen.findByRole('link', { name: 'Основной бот' })).toBeInTheDocument();
  });
});
