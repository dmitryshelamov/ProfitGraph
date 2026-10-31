import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useState } from 'react';

import {
  applyView,
  createScanPreset,
  deleteScanPreset,
  draftInput,
  draftOf,
  draftProblem,
  emptyDraft,
  PresetAlerts,
  type PresetDraft,
  PresetEditor,
  presetTradeSide,
  ScanAutoTrades,
  ScanBacktestList,
  scannerKeys,
  ScannerTable,
  type ScanPreset,
  scanPresetsQuery,
  scanSnapshotQuery,
  scanVenuesQuery,
  startScanBacktest,
  updateScanPreset,
  useScanSnapshotUpdates,
} from '@/modules/scanner';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';

const selectClass =
  'rounded-md border border-line-strong bg-panel px-3 py-2 text-sm text-text outline-none focus:border-link';

function presetKey(preset: ScanPreset): string {
  return preset.id ?? `builtin:${preset.name}`;
}

/** The market scanner (ADR 0059): a venue's shared snapshot filtered and sorted by a preset, updated every minute. */
export function ScannerPage() {
  const queryClient = useQueryClient();
  const venues = useQuery(scanVenuesQuery());
  const [venue, setVenue] = useState<string | undefined>(undefined);
  const current = venue ?? venues.data?.[0]?.venue;
  const snapshot = useQuery({
    ...scanSnapshotQuery(current ?? ''),
    enabled: current !== undefined,
  });
  const presets = useQuery({ ...scanPresetsQuery(current ?? ''), enabled: current !== undefined });
  const [selected, setSelected] = useState<string | null>(null);
  const [draft, setDraft] = useState<PresetDraft | null>(null);

  useScanSnapshotUpdates(current);

  useEffect(() => {
    const first = presets.data?.[0];

    if (draft === null && first) {
      setSelected(presetKey(first));
      setDraft(draftOf(first));
    }
  }, [draft, presets.data]);

  const view = draft ?? emptyDraft;
  const rows = useMemo(() => applyView(snapshot.data?.rows ?? [], view), [snapshot.data, view]);
  const problem = draft ? draftProblem(draft) : null;

  const save = useMutation({
    mutationFn: async (asNew: boolean) => {
      const input = draftInput(view);

      return view.presetId && !asNew
        ? updateScanPreset(view.presetId, input)
        : createScanPreset(current!, input);
    },
    onSuccess: async (preset) => {
      await queryClient.invalidateQueries({ queryKey: scannerKeys.presets(current!) });
      setSelected(presetKey(preset));
      setDraft(draftOf(preset));
    },
  });

  const navigate = useNavigate();
  const [daysBack, setDaysBack] = useState(0);
  const backtest = useMutation({
    mutationFn: () => startScanBacktest(current!, draftInput(view), daysBack),
    onSuccess: (started) =>
      navigate({ to: '/scanner/backtests/$backtestId', params: { backtestId: started.id } }),
  });

  const remove = useMutation({
    mutationFn: (presetId: string) => deleteScanPreset(presetId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: scannerKeys.presets(current!) });
      setSelected(null);
      setDraft(null);
    },
  });

  function choose(key: string) {
    const preset = presets.data?.find((item) => presetKey(item) === key);

    if (preset) {
      setSelected(key);
      setDraft(draftOf(preset));
      save.reset();
    }
  }

  if (venues.isSuccess && venues.data.length === 0) {
    return (
      <div className="mx-auto max-w-5xl">
        <PageHeader title="Сканер" />
        <p className="text-muted">Ни у одной биржи нет сканера.</p>
      </div>
    );
  }

  const total = snapshot.data?.rows.length ?? 0;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Сканер"
        meta={
          snapshot.data?.computedAt ? (
            <>
              Снимок <DateTime value={snapshot.data.computedAt} />, обновляется каждую минуту.
              Показано {rows.length} из {total} пар.
            </>
          ) : (
            'Сканер ещё не прислал ни одного снимка: после запуска он прогревается несколько минут.'
          )
        }
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {venues.data && venues.data.length > 1 && (
              <select
                aria-label="Биржа"
                className={selectClass}
                value={current}
                onChange={(event) => {
                  setVenue(event.target.value);
                  setSelected(null);
                  setDraft(null);
                }}
              >
                {venues.data.map((item) => (
                  <option key={item.venue} value={item.venue}>
                    {item.name}
                  </option>
                ))}
              </select>
            )}
            <select
              aria-label="Пресет"
              className={selectClass}
              value={selected ?? ''}
              onChange={(event) => choose(event.target.value)}
            >
              {selected === null && <option value="">Новый пресет</option>}
              {presets.data?.map((preset) => (
                <option key={presetKey(preset)} value={presetKey(preset)}>
                  {preset.builtIn ? `${preset.name} (встроенный)` : preset.name}
                </option>
              ))}
            </select>
          </div>
        }
      />
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <Card>
          {snapshot.isError ? (
            <p role="alert" className="text-danger-text">
              Не удалось загрузить снимок.
            </p>
          ) : rows.length === 0 ? (
            <p className="text-muted">
              {snapshot.isPending ? 'Загрузка…' : 'Ни одна пара не подходит под условия.'}
            </p>
          ) : (
            <ScannerTable
              venue={current!}
              rows={rows}
              sortMetric={view.sortMetric}
              sortDescending={view.sortDescending}
              trades={presetTradeSide(view.tradeDirection)}
              preset={view.name.trim() || undefined}
              onSort={(metric) =>
                setDraft({
                  ...view,
                  sortMetric: metric,
                  sortDescending: metric === view.sortMetric ? !view.sortDescending : true,
                })
              }
            />
          )}
        </Card>
        <Card title="Пресет">
          <div className="space-y-3">
            <PresetEditor
              draft={view}
              onChange={setDraft}
              onSave={() => save.mutate(false)}
              onSaveAsNew={() => save.mutate(true)}
              onDelete={() => view.presetId && remove.mutate(view.presetId)}
              saving={save.isPending || remove.isPending}
              problem={problem}
            />
            <div className="space-y-2 border-t border-line pt-3">
              <label className="flex items-center gap-2 text-sm">
                <span className="text-text-secondary">Период</span>
                <select
                  className="rounded-md border border-line-strong bg-panel px-2 py-1 text-sm"
                  value={daysBack}
                  onChange={(event) => setDaysBack(Number(event.target.value))}
                >
                  <option value={0}>последние 30 дней</option>
                  <option value={30}>30–60 дней назад</option>
                  <option value={60}>60–90 дней назад</option>
                  <option value={90}>90–120 дней назад</option>
                </select>
              </label>
              <Button
                disabled={backtest.isPending || problem !== null}
                onClick={() => backtest.mutate()}
              >
                {backtest.isPending ? 'Запуск…' : 'Проверить на истории (30 дней)'}
              </Button>
              <p className="text-xs text-muted">
                Что было с парами после входа в результат пресета: через 15 мин, 1 ч, 4 ч и 24 ч.
                Более ранний период нужен автосделкам: правило доказывается двумя проверками, где
                рынок шёл в разные стороны.
              </p>
              {backtest.isError && (
                <ApiErrorMessage error={backtest.error} fallback="Не удалось запустить проверку." />
              )}
              {current && <ScanBacktestList venue={current} />}
            </div>
            <div className="border-t border-line pt-3">
              <PresetAlerts
                preset={presets.data?.find(
                  (preset) => preset.id !== null && preset.id === view.presetId,
                )}
              />
            </div>
            <section aria-label="Автосделки" className="space-y-2 border-t border-line pt-3">
              <h3 className="text-sm font-medium">Автосделки</h3>
              <ScanAutoTrades
                preset={presets.data?.find(
                  (preset) => preset.id !== null && preset.id === view.presetId,
                )}
                supportsShort={
                  venues.data?.find((item) => item.venue === current)?.supportsShort ?? false
                }
              />
            </section>
            {save.isError && (
              <ApiErrorMessage error={save.error} fallback="Не удалось сохранить пресет." />
            )}
            {remove.isError && (
              <ApiErrorMessage error={remove.error} fallback="Не удалось удалить пресет." />
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}
