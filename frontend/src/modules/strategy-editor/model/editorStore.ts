import { create } from 'zustand';

import type { StrategyDefinition, StrategyLayout } from '@/modules/strategies';

export interface Draft {
  readonly name: string;
  readonly definition: StrategyDefinition;
  readonly layout: StrategyLayout;
}

export interface EditorCommand {
  readonly label: string;
  readonly historyGroup?: string;
  apply(draft: Draft): Draft;
}

export interface PickerExit {
  readonly nodeId: string;
  readonly port: string;
}

interface EditorState {
  readonly strategyId: string | null;
  readonly baseVersion: number | null;
  readonly draft: Draft | null;
  readonly savedDraft: Draft | null;
  readonly dirty: boolean;
  readonly selectedNodeId: string | null;
  /** The block exit whose "add block" list is open, from the board or from the block panel. */
  readonly picker: PickerExit | null;
  readonly past: readonly Draft[];
  readonly future: readonly Draft[];
  readonly historyGroup: string | null;
  endHistoryGroup(): void;
  load(strategyId: string, version: number, draft: Draft, force?: boolean): void;
  execute(command: EditorCommand): void;
  apply(change: (draft: Draft) => Draft): void;
  select(nodeId: string | null): void;
  openPicker(exit: PickerExit | null): void;
  undo(): void;
  redo(): void;
  markSaved(version: number, submitted: Draft, saved?: Draft): void;
}

const same = (a: Draft | null, b: Draft | null) => JSON.stringify(a) === JSON.stringify(b);

export const useEditorStore = create<EditorState>()((set, get) => ({
  strategyId: null,
  baseVersion: null,
  draft: null,
  savedDraft: null,
  dirty: false,
  selectedNodeId: null,
  picker: null,
  past: [],
  future: [],
  historyGroup: null,
  endHistoryGroup() {
    if (get().historyGroup !== null) {
      set({ historyGroup: null });
    }
  },
  load(strategyId, version, draft, force = false) {
    get().endHistoryGroup();
    if (
      !force &&
      get().strategyId === strategyId &&
      (get().dirty || get().baseVersion === version)
    ) {
      return;
    }
    set({
      strategyId,
      baseVersion: version,
      draft,
      savedDraft: draft,
      dirty: false,
      selectedNodeId: null,
      picker: null,
      past: [],
      future: [],
    });
  },
  execute(command) {
    const state = get();
    const historyGroup = command.historyGroup ?? null;
    if (!state.draft) {
      return;
    }
    const draft = command.apply(state.draft);
    if (same(draft, state.draft)) {
      if (historyGroup !== state.historyGroup) {
        get().endHistoryGroup();
      }
      return;
    }
    set({
      draft,
      historyGroup,
      past:
        historyGroup !== null && historyGroup === state.historyGroup
          ? state.past
          : [...state.past, state.draft].slice(-100),
      future: [],
      dirty: !same(draft, state.savedDraft),
    });
  },
  apply(change) {
    get().endHistoryGroup();
    const state = get();
    if (!state.draft) {
      return;
    }
    const draft = change(state.draft);
    if (same(draft, state.draft)) {
      return;
    }
    set({
      draft,
      past: [...state.past, state.draft].slice(-100),
      future: [],
      dirty: !same(draft, state.savedDraft),
    });
  },
  select(selectedNodeId) {
    if (get().selectedNodeId === selectedNodeId) {
      return;
    }
    set({ selectedNodeId });
  },
  openPicker(picker) {
    set({ picker });
  },
  undo() {
    get().endHistoryGroup();
    const state = get();
    const draft = state.past.at(-1);
    if (!draft || !state.draft) {
      return;
    }
    set({
      draft,
      past: state.past.slice(0, -1),
      future: [state.draft, ...state.future],
      dirty: !same(draft, state.savedDraft),
    });
  },
  redo() {
    get().endHistoryGroup();
    const state = get();
    const draft = state.future[0];
    if (!draft || !state.draft) {
      return;
    }
    set({
      draft,
      past: [...state.past, state.draft],
      future: state.future.slice(1),
      dirty: !same(draft, state.savedDraft),
    });
  },
  markSaved(version, submitted, saved = submitted) {
    get().endHistoryGroup();
    const state = get();
    const draft = state.draft === submitted ? saved : state.draft;
    const normalizeSubmitted = (entry: Draft) => (entry === submitted ? saved : entry);
    set({
      baseVersion: version,
      draft,
      savedDraft: saved,
      past: state.past.map(normalizeSubmitted),
      future: state.future.map(normalizeSubmitted),
      dirty: !same(draft, saved),
    });
  },
}));
