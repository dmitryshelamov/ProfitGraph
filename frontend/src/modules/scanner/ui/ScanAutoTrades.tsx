import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { type FormEvent, useEffect, useState } from 'react';

import { ApiError } from '@/shared/api/client';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import {
  saveScanAutoTradeRule,
  scanAutoTradeQuery,
  scannerKeys,
  setScanAutoTradeMode,
} from '../api/scannerApi';
import {
  autoTradeModeLabels,
  autoTradeRequestLabels,
  overall,
  paperTradeLabels,
} from '../model/autoTrades';
import { formatMetric } from '../model/format';
import { tradeDirectionLabel, tradeScenarioLabels } from '../model/trades';
import type {
  ScanAutoTrade,
  ScanAutoTradeBacktest,
  ScanAutoTradeRule,
  ScanAutoTradeThird,
  ScanPreset,
} from '../model/types';

const scenarios = ['Levels', 'Target2R', 'Pullback', 'Extreme'] as const;
const timeframes = ['15m', '1h', '4h'] as const;
const input = 'w-full rounded-md border border-line-strong bg-panel px-2 py-1 text-sm';

interface Draft {
  direction: string;
  scenario: string;
  timeframe: string;
  minExpectedR: string;
  minChance: string;
  maxOpenTrades: string;
  maxTradesPerDay: string;
  dailyLossLimit: string;
}

/**
 * Auto trades of a saved preset (ADR 0071): which card of each signal to take and the least expected R it needs, the backtests of
 * the preset's conditions that would prove it, and the switch to paper, which the backend allows only when the card earned in each
 * third of two backtests of opposite markets. While on paper it lists the signals with what came of them and the paper trades with
 * their result next to the backtest's. Live trading is not offered here.
 */
export function ScanAutoTrades({
  preset,
  supportsShort,
}: {
  preset: ScanPreset | undefined;
  supportsShort: boolean;
}) {
  if (!preset?.id || preset.builtIn) {
    return (
      <p className="text-sm text-muted">
        Автосделки настраиваются у сохранённого пресета: сохраните его или копию встроенного.
      </p>
    );
  }

  return <AutoTrades presetId={preset.id} preset={preset} supportsShort={supportsShort} />;
}

function AutoTrades({
  presetId,
  preset,
  supportsShort,
}: {
  presetId: string;
  preset: ScanPreset;
  supportsShort: boolean;
}) {
  const queryClient = useQueryClient();
  const autoTrade = useQuery(scanAutoTradeQuery(presetId));
  const refresh = () =>
    queryClient.invalidateQueries({ queryKey: scannerKeys.autoTrade(presetId) });
  const save = useMutation({
    mutationFn: (draft: Draft) => saveScanAutoTradeRule(presetId, toInput(draft)),
    onSuccess: refresh,
  });
  const mode = useMutation({
    mutationFn: (next: 'Off' | 'Paper') => setScanAutoTradeMode(presetId, next),
    onSuccess: refresh,
  });
  const directions = allowedDirections(preset.tradeDirection, supportsShort);

  if (!autoTrade.data) {
    return autoTrade.isError ? (
      <ApiErrorMessage error={autoTrade.error} fallback="Не удалось загрузить автосделки." />
    ) : (
      <p className="text-sm text-muted">Загружаю автосделки…</p>
    );
  }

  const data = autoTrade.data;
  const rule = data.rule ?? null;

  return (
    <div className="space-y-3">
      <RuleForm
        rule={rule}
        directions={directions}
        saving={save.isPending}
        onSave={(draft) => save.mutate(draft)}
      />
      {save.isError && (
        <ApiErrorMessage error={save.error} fallback="Не удалось сохранить правило." />
      )}
      {rule && (
        <div className="space-y-1">
          <p className="text-sm">
            Режим:{' '}
            <span className="font-medium">{autoTradeModeLabels[rule.mode] ?? rule.mode}</span>
            {rule.pausedOn &&
              ` · на паузе до конца ${rule.pausedOn} (UTC): дневной убыток достигнут`}
          </p>
          <div className="flex gap-2">
            {rule.mode === 'Off' ? (
              <Button size="sm" disabled={mode.isPending} onClick={() => mode.mutate('Paper')}>
                Включить на бумаге
              </Button>
            ) : (
              <Button
                size="sm"
                variant="ghost"
                disabled={mode.isPending}
                onClick={() => mode.mutate('Off')}
              >
                Выключить
              </Button>
            )}
          </div>
          {mode.isError &&
            (mode.error instanceof ApiError && mode.error.code === 'scan_auto_trade_not_proven' ? (
              <p role="alert" className="text-sm text-danger-text">
                Правило не доказано: его карточки должны заработать в каждой трети двух проверок
                условий пресета — последней и более ранней, где рынок шёл в другую сторону.
              </p>
            ) : (
              <ApiErrorMessage error={mode.error} fallback="Не удалось переключить режим." />
            ))}
          <p className="text-xs text-muted">
            Сегодня: открыто {data.openTrades}, создано {data.todayTrades}, убыток{' '}
            {formatMetric(data.todayLoss, 2)} из {formatMetric(rule.dailyLossLimit, 2)}.
          </p>
        </div>
      )}
      <Evidence data={data} rule={rule} />
      {rule && <Activity data={data} />}
    </div>
  );
}

function RuleForm({
  rule,
  directions,
  saving,
  onSave,
}: {
  rule: ScanAutoTradeRule | null;
  directions: readonly string[];
  saving: boolean;
  onSave: (draft: Draft) => void;
}) {
  const [draft, setDraft] = useState<Draft>(() => fromRule(rule, directions));

  useEffect(() => {
    setDraft(fromRule(rule, directions));
  }, [rule?.id, rule?.modeSince]);

  function set(field: keyof Draft, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    onSave(draft);
  }

  return (
    <form
      aria-label="Правило автосделок"
      className="grid grid-cols-2 gap-2 text-sm"
      onSubmit={submit}
    >
      <label className="space-y-1">
        <span className="text-xs text-text-secondary">Направление</span>
        <select
          className={input}
          value={draft.direction}
          onChange={(event) => set('direction', event.target.value)}
        >
          {directions.map((direction) => (
            <option key={direction} value={direction}>
              {tradeDirectionLabel(direction)}
            </option>
          ))}
        </select>
      </label>
      <label className="space-y-1">
        <span className="text-xs text-text-secondary">Сценарий</span>
        <select
          className={input}
          value={draft.scenario}
          onChange={(event) => set('scenario', event.target.value)}
        >
          {scenarios.map((scenario) => (
            <option key={scenario} value={scenario}>
              {tradeScenarioLabels[scenario]}
            </option>
          ))}
        </select>
      </label>
      <label className="space-y-1">
        <span className="text-xs text-text-secondary">ТФ уровней</span>
        <select
          className={input}
          value={draft.timeframe}
          onChange={(event) => set('timeframe', event.target.value)}
        >
          {timeframes.map((timeframe) => (
            <option key={timeframe}>{timeframe}</option>
          ))}
        </select>
      </label>
      <Field
        label="Ожид. R не ниже"
        value={draft.minExpectedR}
        onChange={(value) => set('minExpectedR', value)}
      />
      <Field
        label="Шанс не ниже (0–1)"
        value={draft.minChance}
        onChange={(value) => set('minChance', value)}
      />
      <Field
        label="Открыто не больше"
        value={draft.maxOpenTrades}
        onChange={(value) => set('maxOpenTrades', value)}
      />
      <Field
        label="Сделок за день"
        value={draft.maxTradesPerDay}
        onChange={(value) => set('maxTradesPerDay', value)}
      />
      <Field
        label="Убыток за день"
        value={draft.dailyLossLimit}
        onChange={(value) => set('dailyLossLimit', value)}
      />
      <div className="col-span-2 flex items-center gap-2">
        <Button type="submit" size="sm" disabled={saving}>
          {rule ? 'Сохранить правило' : 'Создать правило'}
        </Button>
        <span className="text-xs text-muted">Изменённое правило выключается.</span>
      </div>
    </form>
  );
}

function Field({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="space-y-1">
      <span className="text-xs text-text-secondary">{label}</span>
      <input
        className={input}
        inputMode="decimal"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  );
}

/** The backtests of the preset's conditions with their kinds of cards at the rule's minimums. */
function Evidence({ data, rule }: { data: ScanAutoTrade; rule: ScanAutoTradeRule | null }) {
  if (!data.latest) {
    return (
      <p className="text-xs text-muted">
        Проверок условий пресета с карточками нет. Запустите проверку на истории: она проведёт
        карточки по правилам бота, и здесь появятся сценарии с их результатом по третям периода.
      </p>
    );
  }

  return (
    <div className="space-y-2">
      <Backtest title="Последняя проверка" backtest={data.latest} rule={rule} />
      {data.otherRegime ? (
        <Backtest title="Проверка обратного рынка" backtest={data.otherRegime} rule={rule} />
      ) : (
        <p className="text-xs text-muted">
          Нужна вторая проверка тех же условий за более ранние 30 дней, когда опорная пара шла в
          другую сторону: одна фаза рынка не доказывает правило.
        </p>
      )}
      {!data.latest.candidates.some((candidate) => candidate.qualifies) && (
        <p role="status" className="text-xs text-warning-text">
          Ни одна карточка не заработала в каждой трети последней проверки при ожид. R не ниже{' '}
          {formatMetric(data.minExpectedR, 2)} — включать нечего.
        </p>
      )}
    </div>
  );
}

function Backtest({
  title,
  backtest,
  rule,
}: {
  title: string;
  backtest: ScanAutoTradeBacktest;
  rule: ScanAutoTradeRule | null;
}) {
  return (
    <div className="space-y-1">
      <p className="text-xs text-text-secondary">
        {title}:{' '}
        <Link
          to="/scanner/backtests/$backtestId"
          params={{ backtestId: backtest.backtestId }}
          className="text-link hover:underline"
        >
          {backtest.presetName}
        </Link>{' '}
        <DateTime value={backtest.from} dateOnly />–<DateTime value={backtest.to} dateOnly />
        {backtest.marketChange !== null && `, рынок ${signed(backtest.marketChange)}%`}
      </p>
      {backtest.candidates.length > 0 && (
        <table className="w-full text-right text-xs">
          <thead className="text-text-secondary">
            <tr>
              <th className="py-1 text-left font-normal">Карточка</th>
              <th className="px-1 font-normal">1/3</th>
              <th className="px-1 font-normal">2/3</th>
              <th className="px-1 font-normal">3/3</th>
              <th className="pl-1 font-normal">Итого</th>
            </tr>
          </thead>
          <tbody>
            {backtest.candidates.map((candidate) => {
              const chosen =
                rule !== null &&
                rule.timeframe === candidate.timeframe &&
                rule.scenario === candidate.scenario &&
                rule.direction === candidate.direction;
              const total = overall(candidate.thirds);

              return (
                <tr
                  key={`${candidate.timeframe}:${candidate.scenario}:${candidate.direction}`}
                  className={chosen ? 'bg-selected' : undefined}
                >
                  <td className="py-1 text-left">
                    {tradeDirectionLabel(candidate.direction)}{' '}
                    {tradeScenarioLabels[candidate.scenario] ?? candidate.scenario}{' '}
                    {candidate.timeframe}
                    {candidate.qualifies && (
                      <span className="ml-1 text-up" title="Заработала в каждой трети">
                        ✓
                      </span>
                    )}
                  </td>
                  {candidate.thirds.map((third, index) => (
                    <td key={index} className="px-1 font-mono">
                      {thirdText(third)}
                    </td>
                  ))}
                  <td className="pl-1 font-mono">
                    {thirdText({ trades: total.trades, meanR: total.meanR })}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}

/** The rule's signals and paper trades. */
function Activity({ data }: { data: ScanAutoTrade }) {
  return (
    <div className="space-y-2 text-xs">
      <p className="text-text-secondary">
        Бумажные сделки: закрыто {data.paperClosed}
        {data.paperMeanR !== null && `, средний R ${signed(data.paperMeanR)}`}.
      </p>
      {data.paperTrades.length > 0 && (
        <ul className="space-y-1">
          {data.paperTrades.map((trade) => (
            <li key={trade.id} className="flex flex-wrap justify-between gap-2">
              <span>
                {trade.symbol} · {tradeDirectionLabel(trade.direction)}{' '}
                {tradeScenarioLabels[trade.scenario] ?? trade.scenario} {trade.timeframe}
              </span>
              <span className="text-muted">
                {paperTradeLabels[trade.status] ?? trade.status}
                {trade.resultR !== null
                  ? ` · ${signed(trade.resultR)} R`
                  : ` · ожид. ${signed(trade.expectedR)} R`}
              </span>
            </li>
          ))}
        </ul>
      )}
      {data.requests.length > 0 && (
        <>
          <p className="text-text-secondary">Сигналы</p>
          <ul className="space-y-1">
            {data.requests.map((request) => (
              <li key={request.id} className="flex flex-wrap justify-between gap-2">
                <span>
                  <DateTime value={request.snapshotAt} /> · {request.symbol}
                </span>
                <span className="text-muted">
                  {autoTradeRequestLabels[request.status] ?? request.status}
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}

function allowedDirections(
  tradeDirection: string | undefined,
  supportsShort: boolean,
): readonly string[] {
  if (tradeDirection === 'Long') {
    return ['Long'];
  }

  if (tradeDirection === 'Short') {
    return ['Short'];
  }

  return supportsShort ? ['Long', 'Short'] : ['Long'];
}

function fromRule(rule: ScanAutoTradeRule | null, directions: readonly string[]): Draft {
  return {
    direction: rule?.direction ?? directions[0] ?? 'Long',
    scenario: rule?.scenario ?? 'Target2R',
    timeframe: rule?.timeframe ?? '1h',
    minExpectedR: rule?.minExpectedR ?? '0.1',
    minChance: rule?.minChance ?? '',
    maxOpenTrades: rule ? String(rule.maxOpenTrades) : '',
    maxTradesPerDay: rule ? String(rule.maxTradesPerDay) : '',
    dailyLossLimit: rule?.dailyLossLimit ?? '',
  };
}

function toInput(draft: Draft) {
  return {
    direction: draft.direction,
    scenario: draft.scenario,
    timeframe: draft.timeframe,
    minExpectedR: draft.minExpectedR.trim(),
    minChance: draft.minChance.trim() || null,
    maxOpenTrades: draft.maxOpenTrades.trim() ? Number(draft.maxOpenTrades) : null,
    maxTradesPerDay: draft.maxTradesPerDay.trim() ? Number(draft.maxTradesPerDay) : null,
    dailyLossLimit: draft.dailyLossLimit.trim() || null,
  };
}

function thirdText(third: ScanAutoTradeThird): string {
  return third.trades === 0 || third.meanR === null
    ? '—'
    : `${signed(third.meanR)} R · ${third.trades}`;
}

function signed(value: string): string {
  const text = formatMetric(value, 2);

  return value.startsWith('-') ? text : `+${text}`;
}
