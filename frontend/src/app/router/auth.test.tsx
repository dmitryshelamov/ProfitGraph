import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { owner, summary } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const signedOut = () => problem(401, 'unauthorized');

describe('authentication', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends an anonymous visitor to sign in and back to the requested screen', async () => {
    let session = false;
    const requests = fakeApi({
      'GET /api/auth/me': () => (session ? json(owner) : signedOut()),
      'POST /api/auth/login': () => {
        session = true;

        return json(owner);
      },
      'GET /api/strategies': () => json([summary]),
      'GET /api/health': () => new Response('Healthy'),
    });
    const { history } = renderApp('/strategies');

    expect(await screen.findByRole('heading', { name: 'Вход' })).toBeInTheDocument();
    expect(history.location.search).toContain('returnTo=%2Fstrategies');

    await userEvent.type(screen.getByLabelText('Логин'), 'owner');
    await userEvent.type(screen.getByLabelText('Пароль'), 'correct-password');
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));

    expect(await screen.findByRole('link', { name: summary.name })).toBeInTheDocument();
    expect(history.location.pathname).toBe('/strategies');
    const login = requests.find((request) => request.url.endsWith('/api/auth/login'))!;
    expect(await login.json()).toEqual({ login: 'owner', password: 'correct-password' });
  });

  it('reports wrong credentials and stays on the sign-in screen', async () => {
    fakeApi({
      'GET /api/auth/me': signedOut,
      'POST /api/auth/login': () => problem(401, 'invalid_credentials'),
      'GET /api/health': () => new Response('Healthy'),
    });
    renderApp('/sign-in');

    await userEvent.type(await screen.findByLabelText('Логин'), 'owner');
    await userEvent.type(screen.getByLabelText('Пароль'), 'wrong-password');
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Неверный логин или пароль.');
  });

  it('reports too many attempts', async () => {
    fakeApi({
      'GET /api/auth/me': signedOut,
      'POST /api/auth/login': () => problem(429, 'too_many_requests'),
      'GET /api/health': () => new Response('Healthy'),
    });
    renderApp('/sign-in');

    await userEvent.type(await screen.findByLabelText('Логин'), 'owner');
    await userEvent.type(screen.getByLabelText('Пароль'), 'password');
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Слишком много попыток');
  });

  it('ignores a return address outside the application', async () => {
    let session = false;
    fakeApi({
      'GET /api/auth/me': () => (session ? json(owner) : signedOut()),
      'POST /api/auth/login': () => {
        session = true;

        return json(owner);
      },
      'GET /api/strategies': () => json([]),
      'GET /api/health': () => new Response('Healthy'),
    });
    const { history } = renderApp('/sign-in?returnTo=%2F%2Fevil.example');

    await userEvent.type(await screen.findByLabelText('Логин'), 'owner');
    await userEvent.type(screen.getByLabelText('Пароль'), 'correct-password');
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));

    expect(await screen.findByText('Стратегий пока нет')).toBeInTheDocument();
    expect(history.location.pathname).toBe('/strategies');
  });

  it('opens the strategy list at the root for a signed-in user', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/strategies': () => json([summary]),
    });
    const { history } = renderApp('/');

    expect(await screen.findByRole('heading', { name: /Стратегии/ })).toBeInTheDocument();
    expect(history.location.pathname).toBe('/strategies');
    expect(screen.getByText('owner')).toBeInTheDocument();
  });

  it('returns to sign-in when the session expires', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/strategies': signedOut,
      'GET /api/health': () => new Response('Healthy'),
    });
    const { history } = renderApp('/strategies');

    expect(await screen.findByRole('heading', { name: 'Вход' })).toBeInTheDocument();
    expect(history.location.search).toContain('returnTo=%2Fstrategies');
  });

  it('signs out and forgets the session', async () => {
    let session = true;
    const requests = fakeApi({
      'GET /api/auth/me': () => (session ? json(owner) : signedOut()),
      'POST /api/auth/logout': () => {
        session = false;

        return new Response(null, { status: 204 });
      },
      'GET /api/strategies': () => json([summary]),
      'GET /api/health': () => new Response('Healthy'),
    });
    const { history } = renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: 'Выйти' }));

    expect(await screen.findByRole('heading', { name: 'Вход' })).toBeInTheDocument();
    expect(history.location.pathname).toBe('/sign-in');
    await waitFor(() =>
      expect(requests.some((request) => request.url.endsWith('/api/auth/logout'))).toBe(true),
    );
  });

  it('keeps a signed-in user away from the sign-in screen', async () => {
    fakeApi({ 'GET /api/auth/me': () => json(owner), 'GET /api/strategies': () => json([]) });
    const { history } = renderApp('/sign-in');

    expect(await screen.findByText('Стратегий пока нет')).toBeInTheDocument();
    expect(history.location.pathname).toBe('/strategies');
  });

  it('shows a server error instead of signing out when the API is down', async () => {
    fakeApi({ 'GET /api/auth/me': () => problem(503, 'unavailable') });
    renderApp('/strategies');

    // One retry with the default one-second delay happens before the error is shown.
    expect(
      await screen.findByRole(
        'heading',
        { name: 'Не удалось связаться с сервером' },
        { timeout: 3_000 },
      ),
    ).toBeInTheDocument();
  });

  it('renders the not-found page for an unknown path', async () => {
    fakeApi({ 'GET /api/auth/me': () => json(owner) });
    renderApp('/missing');

    expect(await screen.findByRole('heading', { name: 'Страница не найдена' })).toBeInTheDocument();
  });
});
