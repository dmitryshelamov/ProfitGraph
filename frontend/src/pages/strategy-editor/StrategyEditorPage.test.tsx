import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { StrictMode } from 'react';

import type { Strategy, StrategyDefinition } from '@/modules/strategies';
import { saveStrategy, strategyKeys, validateStrategy } from '@/modules/strategies';
import { useEditorStore } from '@/modules/strategy-editor';
import { ApiError } from '@/shared/api/client';

import { shouldBlockStrategyExit, StrategyEditorPage } from './StrategyEditorPage';

const navigate = vi.hoisted(() => vi.fn());

const routerNavigation = vi.hoisted((): { request: (to: string) => void } => ({
  request: vi.fn(),
}));

vi.mock('@/modules/bots', () => ({
  liveImpactQuery: (strategyId: string) => ({
    queryKey: ['strategies', strategyId, 'live-impact'],
    queryFn: () =>
      Promise.resolve([
        {
          botId: 'b1',
          venue: 'bybit',
          symbol: 'BTCUSDT',
          timeframe: '1h',
          strategyVersionId: 'v1',
          runtimeStatus: 'Running',
        },
      ]),
  }),
}));

vi.mock('@tanstack/react-router', async () => {
  const { useState } = await import('react');
  return {
    getRouteApi: () => ({ useParams: () => ({ strategyId: 'cached-strategy' }) }),
    useNavigate: () => navigate,
    useBlocker: (options: {
      shouldBlockFn: (paths: {
        current: { pathname: string };
        next: { pathname: string };
      }) => boolean;
    }) => {
      const [destination, setDestination] = useState<string | null>(null);
      routerNavigation.request = (to) => {
        if (
          options.shouldBlockFn({
            current: { pathname: '/strategies/cached-strategy' },
            next: { pathname: to },
          })
        ) {
          setDestination(to);
        } else {
          navigate({ to });
        }
      };
      return {
        status: destination === null ? 'idle' : 'blocked',
        reset: () => setDestination(null),
        proceed: () => {
          navigate({ to: destination });
          setDestination(null);
        },
      };
    },
    Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
      <a
        href={to}
        onClick={(event) => {
          event.preventDefault();
          routerNavigation.request(to);
        }}
      >
        {children}
      </a>
    ),
  };
});
vi.mock('@/modules/strategies', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/modules/strategies')>()),
  saveStrategy: vi.fn(),
  validateStrategy: vi.fn(),
}));

describe('unsaved strategy navigation', () => {
  it('blocks only a dirty editor leaving its pathname', () => {
    expect(shouldBlockStrategyExit(true, '/strategies/one', '/')).toBe(true);
    expect(shouldBlockStrategyExit(true, '/strategies/one', '/backtests')).toBe(true);
    expect(shouldBlockStrategyExit(true, '/strategies/one', '/strategies/one')).toBe(false);
    expect(shouldBlockStrategyExit(false, '/strategies/one', '/')).toBe(false);
  });
});
vi.mock('@/modules/strategy-editor', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/modules/strategy-editor')>()),
  FlowchartInspector: () => null,
  FlowchartEditor: ({ definition }: { definition: StrategyDefinition }) => (
    <output aria-label="Блок-схема стратегии">{JSON.stringify(definition.blocks)}</output>
  ),
}));
vi.mock('@/modules/optimizations', () => ({
  StrategyOptimizationsPanel: ({ strategyId }: { strategyId: string }) => (
    <section aria-label="Оптимизации стратегии">Оптимизации {strategyId}</section>
  ),
}));
vi.mock('@/modules/backtests', () => ({
  StrategyRunsPanel: ({ strategyId }: { strategyId: string }) => (
    <section aria-label="Прогоны стратегии">Прогоны {strategyId}</section>
  ),
}));

const strategy: Strategy = {
  id: 'cached-strategy',
  name: 'Cached SMA',
  currentVersion: 1,
  createdAt: '2026-10-05T00:00:00Z',
  updatedAt: '2026-10-05T00:00:00Z',
  definition: {
    parameters: [{ name: 'fastPeriod', valueType: 'Integer', defaultValue: '2' }],
    values: [],
    blocks: [{ id: 'clock', kind: 'event', source: 'OnCandleClosed', checks: [] }],
    wires: [],
    templateKind: null,
  },
  layout: { positions: {} },
};

function renderCachedEditor() {
  const client = new QueryClient({
    defaultOptions: { queries: { staleTime: Infinity, retry: false } },
  });
  client.setQueryData(strategyKeys.detail(strategy.id), strategy);
  client.setQueryData(strategyKeys.blockCatalog, {
    blockKinds: [],
    eventSources: [],
    checkTypes: [],
    orderKinds: [],
    valueSources: [],
  });
  render(
    <StrictMode>
      <QueryClientProvider client={client}>
        <StrategyEditorPage />
      </QueryClientProvider>
    </StrictMode>,
  );
  return client;
}

describe('cached editor', () => {
  beforeEach(() => {
    navigate.mockReset();
    vi.mocked(saveStrategy).mockReset();
    vi.mocked(validateStrategy).mockReset();
    useEditorStore.setState({
      strategyId: null,
      baseVersion: null,
      draft: null,
      savedDraft: null,
      dirty: false,
      selectedNodeId: null,
      past: [],
      future: [],
    });
  });
  afterEach(cleanup);

  it('shows recreation guidance for old format without an ineffective reload button', async () => {
    vi.mocked(saveStrategy).mockRejectedValue(
      new ApiError(409, 'unsupported_strategy_format', 'Old format'),
    );
    const client = renderCachedEditor();
    act(() => useEditorStore.getState().apply((draft) => ({ ...draft, name: 'Changed' })));
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Создайте её заново'));
    expect(screen.getByRole('link', { name: 'К шаблонам стратегий' })).toHaveAttribute(
      'href',
      '/strategies',
    );
    expect(screen.queryByRole('button', { name: /Загрузить актуальную/ })).not.toBeInTheDocument();
    client.clear();
  });

  it('keeps dirty-draft confirmation usable from old-format recreation guidance', async () => {
    vi.mocked(saveStrategy).mockRejectedValue(
      new ApiError(409, 'unsupported_strategy_format', 'Old format'),
    );
    const client = renderCachedEditor();
    act(() => useEditorStore.getState().apply((draft) => ({ ...draft, name: 'Changed' })));
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
    const recreationLink = await screen.findByRole('link', { name: 'К шаблонам стратегий' });

    fireEvent.click(recreationLink);
    expect(screen.getByRole('dialog')).toHaveTextContent('Есть несохранённые изменения');
    expect(navigate).not.toHaveBeenCalled();
    expect(useEditorStore.getState().dirty).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Остаться' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(useEditorStore.getState().draft?.name).toBe('Changed');

    fireEvent.click(recreationLink);
    fireEvent.click(screen.getByRole('button', { name: 'Уйти' }));
    expect(navigate).toHaveBeenCalledWith({ to: '/strategies' });
    expect(useEditorStore.getState().dirty).toBe(false);
    expect(useEditorStore.getState().draft?.name).toBe(strategy.name);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    client.clear();
  });

  it('opens a positionless graph as lanes without changing the draft', () => {
    const client = renderCachedEditor();
    expect(screen.getByLabelText('Блок-схема стратегии')).toHaveTextContent('clock');
    expect(useEditorStore.getState().dirty).toBe(false);
    client.clear();
  });

  it('keeps layout edits in a positionless cached graph', () => {
    const client = renderCachedEditor();
    const editedLayout = { positions: { fastPeriod: { x: 7, y: 8 } } };
    act(() => {
      useEditorStore.getState().apply((draft) => ({ ...draft, layout: editedLayout }));
    });
    expect(useEditorStore.getState().draft?.layout).toEqual(editedLayout);
    expect(useEditorStore.getState().dirty).toBe(true);
    client.clear();
  });

  it('shows the saved version with undo and redo shortcuts', () => {
    const client = renderCachedEditor();
    expect(screen.getByText('v1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Отменить' })).toHaveAttribute(
      'title',
      'Отменить (Ctrl/⌘+Z)',
    );
    expect(screen.getByRole('button', { name: 'Повторить' })).toHaveAttribute(
      'title',
      'Повторить (Ctrl+Y / ⌘+Shift+Z)',
    );
    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeDisabled();
    expect(document.body.textContent).not.toMatch(/\b(?:quote|base|Train)\b/);
    client.clear();
  });
  it('opens the bounded diagnostics tab after a check with problems', async () => {
    vi.mocked(validateStrategy).mockResolvedValue({
      isValid: false,
      version: null,
      diagnostics: [
        {
          code: 'bad_graph',
          message: 'Ошибка графа',
          blockId: 'clock',
          valueName: null,
          parameterName: null,
          setting: null,
        },
      ],
    });
    const client = renderCachedEditor();
    expect(screen.getByRole('tab', { name: 'Ошибки' })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Ошибки (0)' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Проверить' }));
    await waitFor(() =>
      expect(screen.getByRole('tab', { name: 'Ошибки (1)' })).toHaveAttribute(
        'aria-selected',
        'true',
      ),
    );
    expect(screen.getByRole('button', { name: 'Проверить' })).toHaveTextContent('1');
    expect(screen.getByRole('region', { name: 'Результаты проверки' })).toHaveTextContent(
      'Ошибка графа',
    );
    expect(screen.getByRole('tabpanel')).toHaveClass('board-sheet');
    client.clear();
  });

  it.each(['name', 'layout', 'definition'] as const)(
    'offers save for an unsaved %s edit',
    (part) => {
      const client = renderCachedEditor();
      act(() => {
        useEditorStore.getState().apply((draft) => ({
          ...draft,
          ...(part === 'name' ? { name: 'Unsaved title' } : {}),
          ...(part === 'layout' ? { layout: { positions: { fastPeriod: { x: 7, y: 8 } } } } : {}),
          ...(part === 'definition' ? { definition: { ...draft.definition, blocks: [] } } : {}),
        }));
      });
      expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled();
      expect(screen.getByText('Есть несохранённые изменения')).toBeInTheDocument();
      expect(navigate).not.toHaveBeenCalled();
      client.clear();
    },
  );
  it('saves the draft as the next version and stays in the editor', async () => {
    vi.mocked(saveStrategy).mockResolvedValue({ ...strategy, name: 'Changed', currentVersion: 2 });
    const client = renderCachedEditor();
    fireEvent.change(screen.getByLabelText('Название стратегии'), { target: { value: 'Changed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
    await waitFor(() => expect(screen.getByText('v2')).toBeInTheDocument());
    expect(vi.mocked(saveStrategy)).toHaveBeenCalledWith(
      expect.objectContaining({ id: strategy.id, expectedVersion: 1, name: 'Changed' }),
    );
    expect(screen.getByText('Все изменения сохранены')).toBeInTheDocument();
    expect(navigate).not.toHaveBeenCalled();
    client.clear();
  });
  it('lists the running bots and saves again once the switch is confirmed', async () => {
    vi.mocked(saveStrategy)
      .mockRejectedValueOnce(new ApiError(409, 'live_impact', 'Running bots switch'))
      .mockResolvedValueOnce({ ...strategy, name: 'Changed', currentVersion: 2 });
    const client = renderCachedEditor();
    fireEvent.change(screen.getByLabelText('Название стратегии'), { target: { value: 'Changed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    const prompt = await screen.findByRole('alertdialog', { name: 'Работающие боты' });
    await waitFor(() => expect(prompt).toHaveTextContent('BTCUSDT 1h'));
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить и переключить ботов' }));

    await waitFor(() => expect(screen.getByText('v2')).toBeInTheDocument());
    expect(vi.mocked(saveStrategy)).toHaveBeenLastCalledWith(
      expect.objectContaining({ confirmLiveImpact: true }),
    );
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    client.clear();
  });
  it('keeps the draft when someone saved a newer version', async () => {
    vi.mocked(saveStrategy).mockRejectedValue(new ApiError(409, 'strategy_changed', 'Conflict'));
    const client = renderCachedEditor();
    fireEvent.change(screen.getByLabelText('Название стратегии'), { target: { value: 'Changed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('Стратегия изменилась в другом окне'),
    );
    expect(useEditorStore.getState().draft?.name).toBe('Changed');
    expect(navigate).not.toHaveBeenCalled();
    client.clear();
  });
});
