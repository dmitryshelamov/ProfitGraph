import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, renderHook, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';

import type { GraphValidation, Strategy } from '@/modules/strategies';
import { saveStrategy, strategyKeys, validateStrategy } from '@/modules/strategies';
import { ApiError } from '@/shared/api/client';

import { useEditorStore } from './editorStore';
import { useStrategyEditor } from './useStrategyEditor';

vi.mock('@/modules/strategies', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/modules/strategies')>()),
  saveStrategy: vi.fn(),
  validateStrategy: vi.fn(),
}));
const strategy: Strategy = {
  id: 's1',
  name: 'Original',
  currentVersion: 1,
  createdAt: '2026-10-05T00:00:00Z',
  updatedAt: '2026-10-05T00:00:00Z',
  definition: {
    parameters: [],
    values: [],
    blocks: [{ id: 'period', kind: 'condition', join: 'all', checks: [] }],
    wires: [],
    templateKind: null,
  },
  layout: { positions: { period: { x: 1, y: 2 } } },
};
const invalid: GraphValidation = {
  version: 1,
  isValid: false,
  diagnostics: [
    {
      code: 'MissingInput',
      message: 'Connect input',
      blockId: 'period',
      valueName: 'in',
      parameterName: null,
      setting: null,
    },
  ],
};
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}
function setup() {
  const client = new QueryClient({
    defaultOptions: { queries: { staleTime: Infinity, retry: false }, mutations: { retry: false } },
  });
  client.setQueryData(strategyKeys.detail(strategy.id), strategy);
  client.setQueryData(strategyKeys.blockCatalog, []);
  const wrapper = ({ children }: PropsWithChildren) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  const hook = renderHook(({ id }) => useStrategyEditor(id), {
    wrapper,
    initialProps: { id: strategy.id },
  });
  return { ...hook, client };
}
function move() {
  useEditorStore
    .getState()
    .apply((draft) => ({ ...draft, layout: { positions: { period: { x: 50, y: 60 } } } }));
}
function changeGraph() {
  useEditorStore
    .getState()
    .apply((draft) => ({ ...draft, definition: { ...draft.definition, blocks: [] } }));
}
describe('strategy editor workflow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
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
  it('treats unsupported format as recreation instead of version reload conflict', async () => {
    vi.mocked(saveStrategy).mockRejectedValue(
      new ApiError(409, 'unsupported_strategy_format', 'Old format'),
    );
    const { result } = setup();
    await waitFor(() => expect(result.current.editor.draft).not.toBeNull());
    act(() => result.current.rename('Changed'));
    await act(async () => {
      await result.current.save();
    });
    expect(result.current.unsupportedFormat).toBe(true);
    expect(result.current.conflict).toBe(false);
    expect(result.current.notice).toContain('заново');
    act(() => result.current.reload());
    expect(result.current.reloading).toBe(false);
  });

  it('saves with Ctrl+S from an input and prevents the browser shortcut', async () => {
    vi.mocked(saveStrategy).mockResolvedValue({ ...strategy, name: 'Shortcut', currentVersion: 2 });
    const { result, client } = setup();
    act(() => result.current.rename('Shortcut'));
    const input = document.createElement('input');
    document.body.append(input);
    const event = new KeyboardEvent('keydown', {
      key: 's',
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    });
    act(() => input.dispatchEvent(event));
    expect(event.defaultPrevented).toBe(true);
    await waitFor(() => expect(result.current.editor.baseVersion).toBe(2));
    expect(saveStrategy).toHaveBeenCalledTimes(1);
    input.remove();
    client.clear();
  });
  it('supports Ctrl+Y redo outside form controls', () => {
    const { result, client } = setup();
    act(() => result.current.rename('Changed'));
    act(() => result.current.editor.undo());
    act(() =>
      window.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'y', ctrlKey: true, bubbles: true }),
      ),
    );
    expect(result.current.editor.draft?.name).toBe('Changed');
    client.clear();
  });
  it('undoes a name edit and includes it in the saved baseline', async () => {
    vi.mocked(saveStrategy).mockResolvedValue({ ...strategy, name: 'Renamed', currentVersion: 2 });
    const { result, client } = setup();
    act(() => result.current.rename('Renamed'));
    expect(result.current.editor.dirty).toBe(true);
    act(() => result.current.editor.undo());
    expect(result.current.editor.draft?.name).toBe('Original');
    expect(result.current.editor.dirty).toBe(false);
    act(() => result.current.editor.redo());
    act(() => {
      void result.current.save();
    });
    await waitFor(() => expect(result.current.editor.baseVersion).toBe(2));
    expect(result.current.editor.dirty).toBe(false);
    expect(saveStrategy).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Renamed', expectedVersion: 1 }),
    );
    client.clear();
  });
  it('keeps graph diagnostics after moving a node and renaming, but clears them on graph edits', async () => {
    vi.mocked(validateStrategy).mockResolvedValue(invalid);
    const { result, client } = setup();
    act(() => result.current.validate());
    await waitFor(() => expect(result.current.diagnostics).toEqual(invalid.diagnostics));
    act(move);
    act(() => result.current.rename('Renamed'));
    expect(result.current.diagnostics).toEqual(invalid.diagnostics);
    act(changeGraph);
    expect(result.current.diagnostics).toEqual([]);
    client.clear();
  });
  it('accepts validation during layout/name edits and ignores a response for an edited graph', async () => {
    const first = deferred<GraphValidation>();
    const second = deferred<GraphValidation>();
    vi.mocked(validateStrategy)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise);
    const { result, client } = setup();
    act(() => result.current.validate());
    await waitFor(() => expect(validateStrategy).toHaveBeenCalledTimes(1));
    act(move);
    act(() => result.current.rename('Renamed'));
    await act(async () => {
      first.resolve(invalid);
    });
    await waitFor(() => expect(result.current.diagnostics).toEqual(invalid.diagnostics));
    act(() => result.current.validate());
    await waitFor(() => expect(validateStrategy).toHaveBeenCalledTimes(2));
    act(changeGraph);
    await act(async () => {
      second.resolve(invalid);
    });
    await waitFor(() => expect(result.current.checking).toBe(false));
    expect(result.current.diagnostics).toEqual([]);
    client.clear();
  });
  it('preserves changes made while saving and undo returns to the submitted saved draft', async () => {
    const response = deferred<Strategy>();
    vi.mocked(saveStrategy).mockReturnValue(response.promise);
    const { result, client } = setup();
    act(() => result.current.rename('Submitted'));
    act(() => {
      void result.current.save();
    });
    await waitFor(() => expect(saveStrategy).toHaveBeenCalledTimes(1));
    act(() => result.current.rename('Edited while saving'));
    await act(async () => {
      response.resolve({ ...strategy, name: 'Submitted', currentVersion: 2 });
    });
    await waitFor(() => expect(result.current.editor.baseVersion).toBe(2));
    expect(result.current.editor.draft?.name).toBe('Edited while saving');
    expect(result.current.editor.dirty).toBe(true);
    act(() => result.current.editor.undo());
    expect(result.current.editor.draft?.name).toBe('Submitted');
    expect(result.current.editor.dirty).toBe(false);
    client.clear();
  });
  it('keeps the draft on a 409 and reloads only after the explicit action', async () => {
    vi.mocked(saveStrategy).mockRejectedValue(new ApiError(409, 'strategy_changed', 'Conflict'));
    const { result, client } = setup();
    const reload = vi
      .spyOn(client, 'fetchQuery')
      .mockResolvedValue({ ...strategy, name: 'External', currentVersion: 2 });
    act(() => result.current.rename('Unsaved'));
    act(() => {
      void result.current.save();
    });
    await waitFor(() => expect(result.current.conflict).toBe(true));
    expect(result.current.editor.draft?.name).toBe('Unsaved');
    expect(reload).not.toHaveBeenCalled();
    act(() => result.current.reload());
    await waitFor(() => expect(result.current.editor.draft?.name).toBe('External'));
    expect(result.current.conflict).toBe(false);
    expect(result.current.editor.dirty).toBe(false);
    client.clear();
  });
  it('does not overwrite edits made while explicit reload is in flight', async () => {
    const response = deferred<Strategy>();
    const { result, client } = setup();
    vi.spyOn(client, 'fetchQuery').mockReturnValue(response.promise);
    act(() => result.current.rename('Unsaved'));
    act(() => result.current.reload());
    act(() => result.current.rename('Newer edit'));
    await act(async () => {
      response.resolve({ ...strategy, name: 'External', currentVersion: 2 });
    });
    await waitFor(() => expect(result.current.reloading).toBe(false));
    expect(result.current.editor.draft?.name).toBe('Newer edit');
    expect(result.current.notice).toContain('Черновик изменился');
    client.clear();
  });
  it('ignores validation and save completions after another strategy is opened', async () => {
    const validation = deferred<GraphValidation>();
    const saving = deferred<Strategy>();
    vi.mocked(validateStrategy).mockReturnValue(validation.promise);
    vi.mocked(saveStrategy).mockReturnValue(saving.promise);
    const { result, client, rerender } = setup();
    act(() => result.current.rename('Submitted'));
    act(() => {
      result.current.validate();
      void result.current.save();
    });
    await waitFor(() => expect(saveStrategy).toHaveBeenCalledTimes(1));
    const other = { ...strategy, id: 's2', name: 'Other strategy', currentVersion: 10 };
    client.setQueryData(strategyKeys.detail(other.id), other);
    rerender({ id: other.id });
    await waitFor(() => expect(result.current.editor.strategyId).toBe(other.id));
    await act(async () => {
      validation.resolve(invalid);
      saving.resolve({ ...strategy, name: 'Submitted', currentVersion: 2 });
    });
    expect(result.current.editor.baseVersion).toBe(10);
    expect(result.current.editor.draft?.name).toBe('Other strategy');
    expect(result.current.diagnostics).toEqual([]);
    expect(result.current.notice).toBeNull();
    client.clear();
  });
  it('trims the saved name and updates the unchanged draft without adding a history entry', async () => {
    vi.mocked(saveStrategy).mockResolvedValue({ ...strategy, name: 'Trimmed', currentVersion: 2 });
    const { result, client } = setup();
    act(() => result.current.rename('  Trimmed  '));
    act(() => {
      void result.current.save();
    });
    await waitFor(() => expect(result.current.editor.baseVersion).toBe(2));
    expect(saveStrategy).toHaveBeenCalledWith(expect.objectContaining({ name: 'Trimmed' }));
    expect(result.current.editor.draft?.name).toBe('Trimmed');
    expect(result.current.editor.savedDraft?.name).toBe('Trimmed');
    expect(result.current.editor.dirty).toBe(false);
    expect(result.current.editor.past).toHaveLength(1);
    act(() => result.current.editor.undo());
    expect(result.current.editor.draft?.name).toBe('Original');
    expect(result.current.editor.dirty).toBe(true);
    act(() => result.current.editor.redo());
    expect(result.current.editor.draft?.name).toBe('Trimmed');
    expect(result.current.editor.dirty).toBe(false);
    client.clear();
  });
  it('keeps a concurrent name edit and normalizes the submitted history baseline', async () => {
    const response = deferred<Strategy>();
    vi.mocked(saveStrategy).mockReturnValue(response.promise);
    const { result, client } = setup();
    act(() => result.current.rename('  Trimmed  '));
    act(() => {
      void result.current.save();
    });
    await waitFor(() => expect(saveStrategy).toHaveBeenCalledTimes(1));
    act(() => result.current.rename('  Newer edit  '));
    await act(async () => {
      response.resolve({ ...strategy, name: 'Trimmed', currentVersion: 2 });
    });
    await waitFor(() => expect(result.current.editor.baseVersion).toBe(2));
    expect(saveStrategy).toHaveBeenCalledWith(expect.objectContaining({ name: 'Trimmed' }));
    expect(result.current.editor.draft?.name).toBe('  Newer edit  ');
    expect(result.current.editor.savedDraft?.name).toBe('Trimmed');
    expect(result.current.editor.dirty).toBe(true);
    act(() => result.current.editor.undo());
    expect(result.current.editor.draft?.name).toBe('Trimmed');
    expect(result.current.editor.dirty).toBe(false);
    act(() => result.current.editor.redo());
    expect(result.current.editor.draft?.name).toBe('  Newer edit  ');
    expect(result.current.editor.dirty).toBe(true);
    client.clear();
  });
});
