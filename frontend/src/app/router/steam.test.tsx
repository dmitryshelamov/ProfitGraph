import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bot, credential } from '@/test/botFixtures';
import { fakeApi, json } from '@/test/fakeApi';
import { owner, strategy, summary } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const user = { ...owner, role: 'User' as const, login: 'trader' };
const steamKey = {
  ...credential,
  id: 'steam-key',
  label: 'Steam',
  venue: 'steam',
  apiKeyLast4: '0001',
};
const enrollment = {
  enrollmentId: 'enrollment-1',
  venue: 'steam',
  fields: [],
  oneTimeSecret: null,
  problem: null,
  credentialId: null,
};

describe('Steam', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('links a new Steam Guard step by step and shows the recovery code once', async () => {
    const sent: unknown[] = [];
    let keys: unknown[] = [];
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json(keys),
      'POST /api/exchange-credentials/enrollments': async (request) => {
        sent.push(await request.json());

        return json({ ...enrollment, step: 'email-code', fields: ['code'] }, 201);
      },
      'POST /api/exchange-credentials/enrollments/enrollment-1/steps': async (request) => {
        const body = (await request.json()) as { step: string };
        sent.push(body);

        if (body.step === 'email-code') {
          return json({ ...enrollment, step: 'recovery-saved', oneTimeSecret: 'R98765' });
        }

        if (body.step === 'recovery-saved') {
          return json({ ...enrollment, step: 'activate', fields: ['code'] });
        }

        keys = [steamKey];

        return json({ ...enrollment, step: 'completed', credentialId: 'steam-key' });
      },
      'GET /api/exchange-credentials/steam-key/venue-status': () =>
        json({ status: 'CoolingDown', nextCheckAt: null }),
      'GET /api/exchange-credentials/steam-key/protection': () =>
        json({ open: false, reason: null, since: null, automaticResumeAt: null }),
    });
    renderApp('/credentials');

    await userEvent.selectOptions(await screen.findByLabelText('Биржа'), 'steam');
    await userEvent.type(screen.getByLabelText('Название ключа'), 'Steam');
    await userEvent.type(screen.getByLabelText('Имя аккаунта Steam'), 'invented-account');
    await userEvent.type(screen.getByLabelText('Пароль Steam'), 'invented-password');
    await userEvent.click(screen.getByRole('button', { name: 'Войти в Steam' }));
    await userEvent.type(await screen.findByLabelText('Код из письма Steam'), 'MAIL1');
    await userEvent.click(screen.getByRole('button', { name: 'Отправить код' }));

    expect(await screen.findByLabelText('Код восстановления')).toHaveTextContent('R98765');
    const next = screen.getByRole('button', { name: 'Продолжить' });
    expect(next).toBeDisabled();
    await userEvent.click(screen.getByLabelText('Я записал код восстановления'));
    await userEvent.click(next);
    expect(screen.queryByText('R98765')).not.toBeInTheDocument();
    await userEvent.type(await screen.findByLabelText('Код активации из письма Steam'), 'ACT12');
    await userEvent.click(screen.getByRole('button', { name: 'Активировать' }));

    expect(await screen.findByText(/Аутентификатор привязан/)).toBeInTheDocument();
    expect(await screen.findByText('…0001')).toBeInTheDocument();
    expect(sent).toEqual([
      {
        venue: 'steam',
        label: 'Steam',
        fields: { accountName: 'invented-account', password: 'invented-password' },
      },
      { venue: 'steam', step: 'email-code', fields: { code: 'MAIL1' } },
      { venue: 'steam', step: 'recovery-saved', fields: {} },
      { venue: 'steam', step: 'activate', fields: { code: 'ACT12' } },
    ]);
  });

  it('tells why an enrollment stopped', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json([]),
      'POST /api/exchange-credentials/enrollments': () =>
        json({ ...enrollment, step: 'stopped', problem: 'authenticator_exists' }, 201),
    });
    renderApp('/credentials');

    await userEvent.selectOptions(await screen.findByLabelText('Биржа'), 'steam');
    await userEvent.type(screen.getByLabelText('Название ключа'), 'Steam');
    await userEvent.type(screen.getByLabelText('Имя аккаунта Steam'), 'invented-account');
    await userEvent.type(screen.getByLabelText('Пароль Steam'), 'invented-password');
    await userEvent.click(screen.getByRole('button', { name: 'Войти в Steam' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('steam-import');
    expect(screen.getByRole('button', { name: 'Привязать другой аккаунт' })).toBeInTheDocument();
  });

  it('shows a Steam account’s Guard state, resumes its protection and gives the Owner the Guard code', async () => {
    const resumed: string[] = [];
    let open = true;
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/exchange-credentials': () => json([steamKey]),
      'GET /api/exchange-credentials/steam-key/venue-status': () =>
        json({ status: 'Checking', nextCheckAt: '2026-10-25T12:15:00Z' }),
      'GET /api/exchange-credentials/steam-key/protection': () =>
        json({
          open,
          reason: open ? 'Captcha' : null,
          since: open ? '2026-10-25T12:00:00Z' : null,
          automaticResumeAt: null,
        }),
      'POST /api/exchange-credentials/steam-key/protection/resume': () => {
        resumed.push('steam-key');
        open = false;

        return new Response(null, { status: 204 });
      },
      'GET /api/exchange-credentials/steam-key/authenticator-code': () =>
        json({ code: 'BWR29', validUntil: '2026-10-25T12:00:30Z' }),
    });
    renderApp('/credentials');

    expect(await screen.findByText(/Доступ к Steam Market проверяется/)).toBeInTheDocument();
    expect(await screen.findByText(/Steam потребовал CAPTCHA/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Возобновить' }));
    await waitFor(() =>
      expect(screen.queryByText(/Steam потребовал CAPTCHA/)).not.toBeInTheDocument(),
    );
    await userEvent.click(screen.getByRole('button', { name: 'Показать код Steam Guard' }));

    expect(await screen.findByLabelText('Код Steam Guard')).toHaveTextContent('BWR29');
    expect(resumed).toEqual(['steam-key']);
  });

  it('keeps the Guard code from users who are not the Owner', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/exchange-credentials': () => json([steamKey]),
      'GET /api/exchange-credentials/steam-key/venue-status': () =>
        json({ status: 'Ready', nextCheckAt: null }),
      'GET /api/exchange-credentials/steam-key/protection': () =>
        json({ open: false, reason: null, since: null, automaticResumeAt: null }),
    });
    renderApp('/credentials');

    expect(await screen.findByText('Готов к торговле.')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Показать код Steam Guard' }),
    ).not.toBeInTheDocument();
  });

  it('creates a Steam bot with the item as Steam names it and its own settings', async () => {
    const created: unknown[] = [];
    fakeApi({
      'GET /api/auth/me': () => json(user),
      'GET /api/bots': () => json([]),
      'GET /api/strategies': () => json([summary]),
      [`GET /api/strategies/${strategy.id}`]: () => json(strategy),
      'GET /api/exchange-credentials': () => json([steamKey]),
      'GET /api/market-data/venues': () =>
        json([
          {
            id: 'steam',
            name: 'Steam Community Market',
            timeframes: ['1h', '1d'],
            executionDefaults: null,
            botSettings: {
              settlementHoldDays: 7,
              marketBuySlippage: '0',
              marketBuyDeadlineSeconds: 120,
            },
          },
        ]),
      'POST /api/bots': async (request) => {
        created.push(await request.json());

        return json(bot, 201);
      },
      [`GET /api/bots/${bot.id}`]: () => json(bot),
    });
    renderApp('/bots');

    await userEvent.click(await screen.findByRole('button', { name: 'Новый бот' }));
    await userEvent.selectOptions(screen.getByLabelText('Стратегия'), strategy.id);
    await userEvent.selectOptions(await screen.findByLabelText('Ключ биржи'), steamKey.id);
    const symbol = screen.getByLabelText('Символ');
    await userEvent.clear(symbol);
    await userEvent.type(symbol, 'KZT:730:Dreams & Nightmares Case');
    const slippage = await screen.findByLabelText('Проскальзывание market buy');
    await userEvent.clear(slippage);
    await userEvent.type(slippage, '0.02');
    await screen.findByLabelText('Снижение для усреднения, %');
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    await waitFor(() => expect(created).toHaveLength(1));
    expect(created[0]).toMatchObject({
      symbol: 'KZT:730:Dreams & Nightmares Case',
      timeframe: '1h',
      venueSettings: {
        settlementHoldDays: 7,
        marketBuySlippage: '0.02',
        marketBuyDeadlineSeconds: 120,
      },
    });
  });
});
