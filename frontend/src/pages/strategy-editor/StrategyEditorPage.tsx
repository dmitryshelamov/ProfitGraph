import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Link, useBlocker } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { StrategyRuns } from '@/modules/backtests';
import type { DraftParameter } from '@/modules/bots';
import { liveImpactQuery, parameterFitText } from '@/modules/bots';
import {
  authoringDiagnosticLabel,
  FlowchartEditor,
  FlowchartInspector,
  StrategyValuesPanel,
  useEditorStore,
  useStrategyEditor,
} from '@/modules/strategy-editor';

const route = getRouteApi('/_authenticated/strategies/$strategyId/edit');

export function shouldBlockStrategyExit(dirty: boolean, currentPath: string, nextPath: string) {
  return dirty && currentPath !== nextPath;
}

export function StrategyEditorPage() {
  const { strategyId } = route.useParams();
  const {
    strategy,
    catalog,
    editor,
    conflict,
    liveImpact,
    cancelLiveImpact,
    unsupportedFormat,
    notice,
    diagnostics,
    fieldErrors,
    saving,
    checking,
    reloading,
    save,
    validate,
    reload,
    rename,
    endRename,
  } = useStrategyEditor(strategyId);
  const blocker = useBlocker({
    shouldBlockFn: ({ current, next }) =>
      shouldBlockStrategyExit(
        useEditorStore.getState().dirty && useEditorStore.getState().strategyId === strategyId,
        current.pathname,
        next.pathname,
      ),
    enableBeforeUnload: () =>
      useEditorStore.getState().dirty && useEditorStore.getState().strategyId === strategyId,
    withResolver: true,
  });
  const [focusRequest, setFocusRequest] = useState<{ nodeId: string; sequence: number }>();
  const [activePanel, setActivePanel] = useState<'diagnostics' | 'values' | 'runs' | null>(null);
  const [hasChecked, setHasChecked] = useState(false);
  useEffect(() => () => useEditorStore.getState().select(null), [strategyId]);
  useEffect(() => {
    setFocusRequest(undefined);
    setActivePanel(null);
    setHasChecked(false);
  }, [strategyId]);

  useEffect(() => {
    if (diagnostics.length > 0 || Object.keys(fieldErrors).length > 0) {
      setActivePanel('diagnostics');
    }
  }, [diagnostics, fieldErrors]);

  const leaveDialog = blocker.status === 'blocked' && (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="leave-strategy-title"
        className="rounded border border-line bg-panel p-6 shadow-xl"
      >
        <p id="leave-strategy-title">Есть несохранённые изменения. Уйти без сохранения?</p>
        <div className="mt-4 flex justify-end gap-3">
          <button
            type="button"
            onClick={blocker.reset}
            className="rounded border border-line px-3 py-1"
          >
            Остаться
          </button>
          <button
            type="button"
            onClick={() => {
              const state = useEditorStore.getState();
              if (
                state.strategyId === strategyId &&
                state.savedDraft &&
                state.baseVersion !== null
              ) {
                state.load(strategyId, state.baseVersion, state.savedDraft, true);
              }
              blocker.proceed();
            }}
            className="rounded bg-accent px-3 py-1 font-semibold text-on-accent"
          >
            Уйти
          </button>
        </div>
      </div>
    </div>
  );

  if (unsupportedFormat) {
    return (
      <section className="space-y-4 p-6">
        {leaveDialog}
        <p role="alert" className="text-danger-text">
          Стратегия создана в старом формате. Создайте её заново из шаблона.
        </p>
        <Link to="/strategies" className="text-link underline">
          К шаблонам стратегий
        </Link>
      </section>
    );
  }
  if (
    strategy.isPending ||
    catalog.isPending ||
    editor.strategyId !== strategyId ||
    !editor.draft
  ) {
    if (strategy.isError || catalog.isError) {
      return (
        <p role="alert" className="p-6 text-danger-text">
          {strategy.error?.message ?? catalog.error?.message}
        </p>
      );
    }
    return (
      <p role="status" className="p-6 text-muted">
        Загрузка редактора…
      </p>
    );
  }
  if (catalog.isError) {
    return (
      <p role="alert" className="p-6 text-danger-text">
        {catalog.error.message}
      </p>
    );
  }
  const draft = editor.draft;
  const selected = draft.definition.blocks.find((block) => block.id === editor.selectedNodeId);
  const dirty = editor.dirty;
  const diagnosticCount =
    diagnostics.length +
    Object.values(fieldErrors).reduce((total, messages) => total + messages.length, 0);
  const name = draft.name;
  return (
    <div className="strategy-board">
      {leaveDialog}
      <FlowchartEditor
        definition={draft.definition}
        catalog={catalog.data}
        diagnostics={diagnostics}
        focusRequest={focusRequest}
      />
      <div className="board-overlay">
        <div className="board-top">
          <header className="board-bar" aria-label="Стратегия">
            <Link
              to="/strategies/$strategyId"
              params={{ strategyId }}
              className="board-button board-home"
            >
              <span aria-hidden="true">‹</span> К стратегии
            </Link>
            <span className="board-divider" aria-hidden="true" />
            <input
              aria-label="Название стратегии"
              value={name}
              maxLength={200}
              onChange={(event) => rename(event.target.value)}
              onBlur={endRename}
              className="board-title"
            />
            <span role="status" className="board-state">
              <span>v{editor.baseVersion}</span> ·{' '}
              <span>{dirty ? 'Есть несохранённые изменения' : 'Все изменения сохранены'}</span>
            </span>
          </header>
          <div className="board-actions" aria-label="Действия редактора">
            <div className="board-bar">
              <button
                type="button"
                title="Отменить (Ctrl/⌘+Z)"
                aria-label="Отменить"
                disabled={!editor.past.length}
                onClick={editor.undo}
                className="board-button board-icon"
              >
                ↶
              </button>
              <button
                type="button"
                title="Повторить (Ctrl+Y / ⌘+Shift+Z)"
                aria-label="Повторить"
                disabled={!editor.future.length}
                onClick={editor.redo}
                className="board-button board-icon"
              >
                ↷
              </button>
              <span className="board-divider" aria-hidden="true" />
              <button
                type="button"
                aria-label="Проверить"
                disabled={checking}
                onClick={() => {
                  setHasChecked(true);
                  validate();
                }}
                className="board-button"
              >
                {checking ? 'Проверка…' : 'Проверить'}
                {hasChecked && (
                  <span aria-hidden="true" className="board-count">
                    {diagnosticCount}
                  </span>
                )}
              </button>
              <div role="tablist" aria-label="Панель стратегии" className="contents">
                {(
                  [
                    ['values', 'Параметры и значения'],
                    ['diagnostics', hasChecked ? `Ошибки (${diagnosticCount})` : 'Ошибки'],
                    ['runs', 'Прогоны'],
                  ] as const
                ).map(([panel, label]) => (
                  <button
                    key={panel}
                    id={`strategy-tab-${panel}`}
                    type="button"
                    role="tab"
                    aria-selected={activePanel === panel}
                    aria-expanded={activePanel === panel}
                    aria-controls="strategy-side-panel"
                    onClick={() => setActivePanel((current) => (current === panel ? null : panel))}
                    className="board-button"
                  >
                    {label}
                  </button>
                ))}
              </div>
              <span className="board-divider" aria-hidden="true" />
              {/* A run replays a saved version, so unsaved edits must be saved first. */}
              {dirty ? (
                <button
                  type="button"
                  disabled
                  title="Сохраните изменения, чтобы запустить прогон"
                  className="board-button"
                >
                  Прогон
                </button>
              ) : (
                <Link
                  to="/strategies/$strategyId/backtest"
                  params={{ strategyId }}
                  className="board-button"
                >
                  Прогон
                </Link>
              )}
              <button
                type="button"
                disabled={saving || reloading || !dirty || !name.trim() || conflict}
                onClick={() => void save()}
                className="board-button board-primary"
              >
                {saving ? 'Сохранение…' : 'Сохранить'}
              </button>
            </div>
          </div>
        </div>
        <div className="board-body">
          {notice && activePanel !== 'diagnostics' && (
            <p role="status" className="board-toast">
              {notice}
            </p>
          )}
          {liveImpact && (
            <LiveImpactPrompt
              strategyId={strategyId}
              parameters={editor.draft?.definition.parameters ?? []}
              saving={saving}
              onConfirm={() => void save(true)}
              onCancel={cancelLiveImpact}
            />
          )}
          {conflict && (
            <div role="alert" className="board-toast board-conflict">
              <span>Стратегия изменилась в другом окне. Загрузка заменит текущий черновик.</span>
              <button
                type="button"
                disabled={reloading}
                onClick={() => void reload()}
                className="board-button"
              >
                Загрузить актуальную версию
              </button>
            </div>
          )}
          {selected && (
            <aside className="board-card board-inspector" aria-label="Настройки блока">
              <FlowchartInspector
                block={selected}
                definition={draft.definition}
                catalog={catalog.data}
                diagnostics={diagnostics}
                fieldErrors={fieldErrors}
                onClose={() => editor.select(null)}
              />
            </aside>
          )}
          <div
            id="strategy-side-panel"
            role="tabpanel"
            aria-labelledby={activePanel ? `strategy-tab-${activePanel}` : undefined}
            hidden={activePanel === null}
            className={`board-card board-sheet${selected ? ' beside-inspector' : ''}`}
          >
            {activePanel && (
              <button
                type="button"
                aria-label="Закрыть панель"
                onClick={() => setActivePanel(null)}
                className="board-button board-icon sheet-close"
              >
                ×
              </button>
            )}
            <div className="screen">
              {activePanel === 'values' && (
                <StrategyValuesPanel
                  definition={draft.definition}
                  catalog={catalog.data}
                  diagnostics={diagnostics}
                />
              )}
              {activePanel === 'runs' && (
                <section aria-label="Прогоны стратегии" className="px-4 py-2 text-sm">
                  <StrategyRuns strategyId={strategyId} />
                </section>
              )}
              {activePanel === 'diagnostics' && (
                <section aria-label="Результаты проверки" className="px-4 py-2 text-sm">
                  {notice && <p role="status">{notice}</p>}
                  {!notice && diagnosticCount === 0 && (
                    <p className="text-muted">Проблем пока нет.</p>
                  )}
                  <ul>
                    {diagnostics.map((item, index) => (
                      <li key={index} className="text-danger-text">
                        <button
                          type="button"
                          disabled={!item.blockId && !item.valueName && !item.parameterName}
                          className="text-left hover:underline"
                          onClick={() => {
                            if (!item.blockId) {
                              setActivePanel('values');
                              return;
                            }
                            editor.select(item.blockId);
                            setFocusRequest((previous) => ({
                              nodeId: item.blockId!,
                              sequence: (previous?.sequence ?? 0) + 1,
                            }));
                          }}
                        >
                          {authoringDiagnosticLabel(item, draft.definition, catalog.data)
                            ? `${authoringDiagnosticLabel(item, draft.definition, catalog.data)}: `
                            : ''}
                          {item.message}
                        </button>
                      </li>
                    ))}
                  </ul>
                  <ul>
                    {Object.entries(fieldErrors).flatMap(([field, messages]) =>
                      messages.map((message, index) => (
                        <li key={`${field}-${index}`} className="text-danger-text">
                          {message}
                        </li>
                      )),
                    )}
                  </ul>
                </section>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/**
 * Running bots switch to a saved version on their next root, so the save asks to confirm after listing them. A parameter the
 * version adds takes its default on every bot and a removed one is dropped (ADR 0039); the prompt names those values.
 */
function LiveImpactPrompt({
  strategyId,
  parameters,
  saving,
  onConfirm,
  onCancel,
}: {
  strategyId: string;
  parameters: readonly DraftParameter[];
  saving: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const bots = useQuery(liveImpactQuery(strategyId));
  const fit = bots.data ? parameterFitText(bots.data, parameters) : null;

  return (
    <div role="alertdialog" aria-label="Работающие боты" className="board-toast board-conflict">
      <span>
        Работающие боты перейдут на новую версию на следующей свече
        {bots.data &&
          bots.data.length > 0 &&
          `: ${bots.data.map((bot) => `${bot.symbol} ${bot.timeframe}`).join(', ')}`}
        .{fit && ` Значения ботов изменятся: ${fit}.`}
      </span>
      <button type="button" disabled={saving} onClick={onConfirm} className="board-button">
        Сохранить и переключить ботов
      </button>
      <button type="button" onClick={onCancel} className="board-button">
        Отмена
      </button>
    </div>
  );
}
