import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { owner, strategy, summary, template } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const strategyPath = `/api/strategies/${strategy.id}`;

function strategyRoutes(validation: unknown = { isValid: true, version: 2, diagnostics: [] }) {
  return {
    'GET /api/auth/me': () => json(owner),
    [`GET ${strategyPath}`]: () => json(strategy),
    [`GET ${strategyPath}/versions`]: () =>
      json([
        { id: 'v2', number: 2, createdAt: '2026-10-24T09:30:00Z' },
        { id: 'v1', number: 1, createdAt: '2026-10-20T08:00:00Z' },
      ]),
    [`POST ${strategyPath}/validate?version=2`]: () => json(validation),
  };
}

describe('strategy list', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists the user’s strategies and filters them by name and template', async () => {
    const other = { ...summary, id: 'other', name: 'Свой SMA', templateKind: null };
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/strategies': () => json([summary, other]),
    });
    renderApp('/strategies');

    const table = await screen.findByRole('table');
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getByText('Ladder SMA')).toBeInTheDocument();
    expect(within(table).getAllByText('v2')).toHaveLength(2);

    await userEvent.click(screen.getByRole('button', { name: 'Ladder SMA' }));
    expect(within(table).queryByText('Свой SMA')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Все' }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по названию' }), 'свой');
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(2);
    expect(screen.getByRole('link', { name: 'Свой SMA' })).toBeInTheDocument();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Поиск по названию' }), 'нет такой');
    expect(screen.getByText('Ничего не найдено')).toBeInTheDocument();
  });

  it('creates a strategy from a template and opens it', async () => {
    const created: unknown[] = [];
    fakeApi({
      ...strategyRoutes(),
      'GET /api/strategies': () => json([]),
      'GET /api/strategy-templates': () => json([template]),
      'POST /api/strategies': async (request) => {
        created.push(await request.json());

        return json(strategy, 201);
      },
    });
    const { history } = renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: 'Создать из шаблона' }));
    await userEvent.click(await screen.findByRole('button', { name: /Лестница с фильтром SMA/ }));
    const name = screen.getByLabelText('Название стратегии');
    expect(name).toHaveValue('Ladder SMA');
    await userEvent.clear(name);
    await userEvent.type(name, '  Ladder на BTC  ');
    await userEvent.click(screen.getByRole('button', { name: 'Создать стратегию' }));

    expect(await screen.findByRole('heading', { name: strategy.name })).toBeInTheDocument();
    expect(history.location.pathname).toBe(`/strategies/${strategy.id}`);
    expect(created).toEqual([
      { name: 'Ladder на BTC', definition: template.definition, layout: template.layout },
    ]);
  });

  it('shows the server reason when creation fails', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      'GET /api/strategies': () => json([]),
      'GET /api/strategy-templates': () => json([template]),
      'POST /api/strategies': () =>
        problem(422, 'invalid_strategy_name', 'Название слишком длинное.'),
    });
    renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: 'Создать из шаблона' }));
    await userEvent.click(await screen.findByRole('button', { name: /Лестница с фильтром SMA/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Создать стратегию' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Название слишком длинное.');
  });
});

describe('strategy page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the saved version, its parameters and history', async () => {
    fakeApi(strategyRoutes());
    renderApp(`/strategies/${strategy.id}`);

    expect(await screen.findByRole('heading', { name: strategy.name })).toBeInTheDocument();
    expect(screen.getByText(/Ladder SMA · версия 2/)).toBeInTheDocument();
    expect(await screen.findByText('Версия 2 без ошибок')).toBeInTheDocument();
    expect(screen.getByText('Снижение для усреднения')).toBeInTheDocument();
    expect(screen.getByText('1.5%')).toHaveAttribute('title', '0.015');
    expect(screen.getByText('25')).toBeInTheDocument();
    const versions = await screen.findByRole('list');
    expect(within(versions).getAllByRole('listitem')).toHaveLength(2);
    expect(within(versions).getByText('текущая')).toBeInTheDocument();
  });

  it('lists validation errors of the saved version', async () => {
    fakeApi(
      strategyRoutes({
        isValid: false,
        version: 2,
        diagnostics: [
          {
            code: 'MissingInput',
            message: 'Блоку не хватает входа.',
            blockId: 'entry',
            valueName: null,
            parameterName: null,
            setting: null,
          },
        ],
      }),
    );
    renderApp(`/strategies/${strategy.id}`);

    expect(await screen.findByText('Ошибок в версии 2: 1')).toBeInTheDocument();
    expect(screen.getByText('Блоку не хватает входа.')).toBeInTheDocument();
    expect(screen.getByText('· блок entry')).toBeInTheDocument();
  });

  it('reports a strategy that does not exist or belongs to someone else', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      [`GET ${strategyPath}`]: () => problem(404, 'strategy_not_found'),
    });
    renderApp(`/strategies/${strategy.id}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Стратегия не найдена.');
    expect(
      within(screen.getByRole('navigation', { name: 'Путь' })).getByRole('link', {
        name: 'Стратегии',
      }),
    ).toBeInTheDocument();
  });

  it('explains a strategy stored in an old format', async () => {
    fakeApi({
      'GET /api/auth/me': () => json(owner),
      [`GET ${strategyPath}`]: () => problem(409, 'unsupported_strategy_format'),
    });
    renderApp(`/strategies/${strategy.id}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('создать заново');
  });
});
