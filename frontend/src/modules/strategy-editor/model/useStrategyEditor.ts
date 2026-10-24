import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';

import type { GraphDiagnostic, Strategy } from '@/modules/strategies';
import {
  blockCatalogQuery,
  saveStrategy,
  strategyKeys,
  strategyQuery,
  validateStrategy,
} from '@/modules/strategies';
import { ApiError } from '@/shared/api/client';

import { renameStrategy } from './editorCommands';
import type { Draft } from './editorStore';
import { useEditorStore } from './editorStore';

const graphKey = (draft: Draft | null) => JSON.stringify(draft?.definition);
const toDraft = (strategy: Strategy): Draft => ({
  name: strategy.name,
  definition: strategy.definition,
  layout: strategy.layout,
});

interface Submission {
  readonly id: string;
  readonly draft: Draft;
  readonly version: number;
}

export function useStrategyEditor(strategyId: string) {
  const strategy = useQuery(strategyQuery(strategyId));
  const catalog = useQuery(blockCatalogQuery());
  const queryClient = useQueryClient();
  const editor = useEditorStore();
  const routeRef = useRef(strategyId);
  routeRef.current = strategyId;
  const [conflict, setConflict] = useState(false);
  const [unsupportedFailure, setUnsupportedFailure] = useState(false);
  const unsupportedFormat =
    unsupportedFailure ||
    (strategy.error instanceof ApiError && strategy.error.code === 'unsupported_strategy_format');
  const [notice, setNotice] = useState<string | null>(null);
  const [diagnostics, setDiagnostics] = useState<readonly GraphDiagnostic[]>([]);
  const [fieldErrors, setFieldErrors] = useState<Readonly<Record<string, readonly string[]>>>({});
  const semanticGraph = graphKey(editor.draft);
  const active = (id: string) =>
    routeRef.current === id && useEditorStore.getState().strategyId === id;
  const unchangedGraph = (submission: Submission) =>
    active(submission.id) &&
    graphKey(useEditorStore.getState().draft) === graphKey(submission.draft);

  const showError = (error: unknown) => {
    if (error instanceof ApiError && error.code === 'unsupported_strategy_format') {
      setUnsupportedFailure(true);
      setConflict(false);
      setNotice('Стратегия создана в старом формате. Создайте её заново из шаблона.');
      return;
    }
    setNotice(error instanceof Error ? error.message : 'Ошибка запроса');
    if (error instanceof ApiError) {
      const errors: Record<string, string[]> = {};
      for (const item of error.errors) {
        (errors[item.field] ??= []).push(item.message);
      }
      setFieldErrors(errors);
    }
  };

  useEffect(() => {
    setConflict(false);
    setUnsupportedFailure(false);
  }, [strategyId]);

  useEffect(() => {
    setDiagnostics([]);
    setFieldErrors({});
    setNotice(null);
  }, [semanticGraph, strategyId]);

  useEffect(() => {
    const data = strategy.data;
    if (!data) {
      return;
    }
    const state = useEditorStore.getState();
    if (state.strategyId === strategyId && state.dirty) {
      return;
    }
    state.load(strategyId, data.currentVersion, toDraft(data));
  }, [strategy.data, strategyId]);

  const saveMutation = useMutation({
    mutationFn: (submission: Submission) =>
      saveStrategy({
        id: submission.id,
        expectedVersion: submission.version,
        ...submission.draft,
        name: submission.draft.name.trim(),
      }),
    onSuccess: (result, submission) => {
      queryClient.setQueryData(strategyKeys.detail(submission.id), result);
      void queryClient.invalidateQueries({ queryKey: strategyKeys.all });
      if (!active(submission.id)) {
        return;
      }
      const unchanged = useEditorStore.getState().draft === submission.draft;
      useEditorStore.getState().markSaved(result.currentVersion, submission.draft, {
        ...submission.draft,
        name: result.name,
      });
      setConflict(false);
      if (unchanged) {
        setNotice('Стратегия сохранена');
      }
    },
    onError: (error, submission) => {
      if (!active(submission.id)) {
        return;
      }
      if (error instanceof ApiError && error.status === 409 && error.code === 'strategy_changed') {
        setConflict(true);
      } else if (useEditorStore.getState().draft === submission.draft) {
        showError(error);
      }
    },
  });

  const validateMutation = useMutation({
    mutationFn: (submission: Submission) =>
      validateStrategy(submission.id, submission.draft.definition),
    onSuccess: (result, submission) => {
      if (!unchangedGraph(submission)) {
        return;
      }
      setDiagnostics(result.diagnostics);
      setNotice(result.isValid ? 'Граф корректен' : 'В графе есть ошибки');
    },
    onError: (error, submission) => {
      if (unchangedGraph(submission)) {
        showError(error);
      }
    },
  });

  const reloadMutation = useMutation({
    mutationFn: (submission: Submission) =>
      queryClient.fetchQuery({ ...strategyQuery(submission.id), staleTime: 0 }),
    onSuccess: (result, submission) => {
      if (!active(submission.id)) {
        return;
      }
      if (useEditorStore.getState().draft !== submission.draft) {
        setNotice('Черновик изменился во время загрузки. Повторите загрузку актуальной версии.');
        return;
      }
      useEditorStore.getState().load(submission.id, result.currentVersion, toDraft(result), true);
      setConflict(false);
    },
    onError: (error, submission) => {
      if (active(submission.id)) {
        showError(error);
      }
    },
  });

  const save = async (): Promise<number | null> => {
    useEditorStore.getState().endHistoryGroup();
    const state = useEditorStore.getState();
    if (
      !state.draft ||
      state.baseVersion === null ||
      state.strategyId !== strategyId ||
      !state.dirty ||
      !state.draft.name.trim() ||
      conflict ||
      unsupportedFormat ||
      saveMutation.isPending ||
      reloadMutation.isPending
    ) {
      return null;
    }
    setNotice(null);
    try {
      const result = await saveMutation.mutateAsync({
        id: strategyId,
        version: state.baseVersion,
        draft: state.draft,
      });
      const current = useEditorStore.getState();
      return current.strategyId === strategyId && !current.dirty ? result.currentVersion : null;
    } catch {
      return null;
    }
  };

  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey)) {
        return;
      }
      const key = event.key.toLowerCase();
      if (key === 's') {
        event.preventDefault();
        void save();
        return;
      }
      if (key !== 'z' && key !== 'y') {
        return;
      }
      if (
        event.target instanceof HTMLElement &&
        (event.target.closest('input,textarea,select') || event.target.isContentEditable)
      ) {
        return;
      }
      event.preventDefault();
      if (key === 'y' || event.shiftKey) {
        useEditorStore.getState().redo();
      } else {
        useEditorStore.getState().undo();
      }
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  });

  const submit = (operation: 'validate' | 'reload') => {
    useEditorStore.getState().endHistoryGroup();
    const state = useEditorStore.getState();
    if (
      unsupportedFormat ||
      !state.draft ||
      state.baseVersion === null ||
      state.strategyId !== strategyId
    ) {
      return;
    }
    const submission = { id: strategyId, version: state.baseVersion, draft: state.draft };
    setNotice(null);
    if (operation === 'validate') {
      setFieldErrors({});
      validateMutation.mutate(submission);
    } else {
      reloadMutation.mutate(submission);
    }
  };

  return {
    strategy,
    catalog,
    editor,
    conflict,
    unsupportedFormat,
    notice,
    diagnostics,
    fieldErrors,
    saving: saveMutation.isPending && saveMutation.variables.id === strategyId,
    checking: validateMutation.isPending && validateMutation.variables.id === strategyId,
    reloading: reloadMutation.isPending && reloadMutation.variables.id === strategyId,
    save,
    validate: () => submit('validate'),
    reload: () => submit('reload'),
    endRename: editor.endHistoryGroup,
    rename: (name: string) => editor.execute(renameStrategy(name)),
  };
}
