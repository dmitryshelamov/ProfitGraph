import { renameStrategy } from './editorCommands';
import type { Draft } from './editorStore';
import { useEditorStore } from './editorStore';

const draft: Draft = {
  name: 'SMA',
  definition: { parameters: [], values: [], blocks: [], wires: [], templateKind: null },
  layout: { positions: {} },
};
const move = (x: number) =>
  useEditorStore.getState().execute({
    label: 'Move node',
    apply: (current) => ({ ...current, layout: { positions: { a: { x, y: 2 } } } }),
  });

describe('editor store', () => {
  beforeEach(() =>
    useEditorStore.setState({
      strategyId: null,
      baseVersion: null,
      draft: null,
      savedDraft: null,
      dirty: false,
      selectedNodeId: null,
      past: [],
      future: [],
      historyGroup: null,
    }),
  );

  it('keeps unsaved edits when the same strategy is refetched', () => {
    useEditorStore.getState().load('s1', 1, draft);
    move(1);
    useEditorStore.getState().load('s1', 2, draft);

    expect(useEditorStore.getState().dirty).toBe(true);
    expect(useEditorStore.getState().baseVersion).toBe(1);
    expect(useEditorStore.getState().draft?.layout.positions.a).toEqual({ x: 1, y: 2 });
  });

  it('replaces the draft and history when another strategy opens', () => {
    useEditorStore.getState().load('s1', 1, draft);
    move(1);
    useEditorStore.getState().select('a');
    useEditorStore.getState().load('s2', 3, draft);

    expect(useEditorStore.getState()).toMatchObject({
      strategyId: 's2',
      baseVersion: 3,
      dirty: false,
      past: [],
      future: [],
      selectedNodeId: null,
    });
  });

  it('undoes and redoes edits, clearing dirty when returning to the saved draft', () => {
    useEditorStore.getState().load('s1', 1, draft);
    move(1);
    useEditorStore.getState().undo();

    expect(useEditorStore.getState().draft).toEqual(draft);
    expect(useEditorStore.getState().dirty).toBe(false);
    useEditorStore.getState().redo();

    expect(useEditorStore.getState().draft?.layout.positions.a).toEqual({ x: 1, y: 2 });
    expect(useEditorStore.getState().dirty).toBe(true);
  });

  it('discards the redo branch when editing after undo', () => {
    useEditorStore.getState().load('s1', 1, draft);
    move(1);
    move(3);
    useEditorStore.getState().undo();
    move(5);
    useEditorStore.getState().redo();

    expect(useEditorStore.getState().future).toEqual([]);
    expect(useEditorStore.getState().draft?.layout.positions.a?.x).toBe(5);
  });

  it('preserves edits made while saving and uses the submitted draft as the new saved baseline', () => {
    useEditorStore.getState().load('s1', 1, draft);
    move(1);
    const submitted = useEditorStore.getState().draft!;
    move(3);
    useEditorStore.getState().markSaved(2, submitted);

    expect(useEditorStore.getState()).toMatchObject({
      baseVersion: 2,
      savedDraft: submitted,
      dirty: true,
    });
    expect(useEditorStore.getState().draft?.layout.positions.a?.x).toBe(3);
    useEditorStore.getState().undo();

    expect(useEditorStore.getState().draft).toEqual(submitted);
    expect(useEditorStore.getState().dirty).toBe(false);
  });

  it('does not add history or dirty state for a command that changes nothing', () => {
    useEditorStore.getState().load('s1', 1, draft);
    useEditorStore.getState().apply((current) => ({ ...current }));

    expect(useEditorStore.getState()).toMatchObject({ dirty: false, past: [], future: [] });
  });

  it('does not notify subscribers when selecting the same node again', () => {
    useEditorStore.getState().load('s1', 1, draft);
    const changed = vi.fn();
    const unsubscribe = useEditorStore.subscribe(changed);
    useEditorStore.getState().select('a');
    const selectedState = useEditorStore.getState();
    useEditorStore.getState().select('a');

    expect(changed).toHaveBeenCalledTimes(1);
    expect(useEditorStore.getState()).toBe(selectedState);
    useEditorStore.getState().select(null);
    useEditorStore.getState().select(null);

    expect(changed).toHaveBeenCalledTimes(2);
    unsubscribe();
  });
  it('coalesces per-letter typing into one undo and redo step', () => {
    const store = useEditorStore.getState();
    store.load('s1', 1, draft);
    ['S', 'SM', 'SMA renamed'].forEach((name) => store.execute(renameStrategy(name)));
    expect(useEditorStore.getState().past).toHaveLength(1);
    store.undo();
    expect(useEditorStore.getState().draft?.name).toBe('SMA');
    expect(useEditorStore.getState().dirty).toBe(false);
    store.redo();
    expect(useEditorStore.getState().draft?.name).toBe('SMA renamed');
  });
  it.each(['blur', 'command', 'load', 'save', 'undo', 'redo'])(
    'ends rename grouping at %s',
    (boundary) => {
      const store = useEditorStore.getState();
      store.load('s1', 1, draft);
      store.execute(renameStrategy('first'));
      store.execute(renameStrategy('first edit'));
      if (boundary === 'blur') {
        store.endHistoryGroup();
      } else if (boundary === 'command') {
        move(3);
      } else if (boundary === 'load') {
        store.load('s1', 1, draft);
      } else if (boundary === 'save') {
        store.markSaved(2, useEditorStore.getState().draft!);
      } else if (boundary === 'undo') {
        store.undo();
      } else {
        store.undo();
        store.redo();
      }
      const before = useEditorStore.getState().draft;
      store.execute(renameStrategy('second'));
      store.execute(renameStrategy('second edit'));
      store.undo();
      expect(useEditorStore.getState().draft).toEqual(before);
    },
  );
  it('keeps a submitted rename as the baseline while later typing remains undoable', () => {
    const store = useEditorStore.getState();
    store.load('s1', 1, draft);
    store.execute(renameStrategy('submitted'));
    const submitted = useEditorStore.getState().draft!;
    store.endHistoryGroup();
    store.execute(renameStrategy('submitted later'));
    store.markSaved(2, submitted);
    store.undo();
    expect(useEditorStore.getState().draft).toEqual(submitted);
    expect(useEditorStore.getState().dirty).toBe(false);
  });
});
