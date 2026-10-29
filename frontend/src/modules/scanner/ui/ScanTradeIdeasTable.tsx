import { useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect, useMemo, useState } from 'react';

import { ApiError } from '@/shared/api/client';
import { addDecimals, compareDecimals } from '@/shared/lib/decimal';
import { Button } from '@/shared/ui/Button';

import { createScanTrade, scannerKeys } from '../api/scannerApi';
import { formatMetric, withQuote } from '../model/format';
import { type IdeaOutcome, ideaOutcome } from '../model/ideaOutcome';
import { tradeDirectionLabel, tradeScenarioHint, tradeScenarioLabels } from '../model/trades';
import type { ScanTradeIdea, ScanTradeIdeas } from '../model/types';
import { useScanQuote } from './useScanQuote';

const createErrors: Record<string, string> = {
  scan_trade_live: 'Такая сделка уже живёт.',
  scan_trade_entry_moved: 'Цена ушла от входа — идеи обновятся.',
  scan_trade_pair_not_scanned: 'Пары нет в последнем снимке сканера.',
  scan_trade_settings_missing: 'Сначала задайте риск и ключ.',
  scan_trade_short_unsupported: 'На этой бирже нет шорта.',
};

type Outcome = { readonly botId: string } | { readonly error: string };
type Direction = 'All' | 'Long' | 'Short';
type Sort = 'none' | 'profit' | 'netRewardRisk';

interface Row {
  readonly key: string;
  readonly idea: ScanTradeIdea;
  readonly outcome: IdeaOutcome;
}

const filterButton =
  'rounded-md border px-2.5 py-1 text-sm aria-pressed:border-link aria-pressed:text-link border-line-strong text-text-secondary hover:text-text';

/**
 * The trade ideas of a pair on every timeframe (ADR 0060) as one table to compare them: each with its entry, target and stop, and
 * what it makes at the target and loses at the stop after fees, in the venue's currency and in percent of the trade. The owner
 * ticks the wanted ones and «Создать выбранные» creates a bot with real orders for each; on futures short ideas (ADR 0061) sell at
 * the entry. Hovering a row, or ticking it, shows the idea on the chart through `onPreview`. «Скрыть» hides an idea until the
 * ideas change; an idea a live trade already took cannot be ticked.
 */
export function ScanTradeIdeasTable({
  venue,
  ideas,
  onPreview,
}: {
  venue: string;
  ideas: ScanTradeIdeas;
  onPreview?: (ideas: readonly ScanTradeIdea[]) => void;
}) {
  const queryClient = useQueryClient();
  const quote = useScanQuote(venue);
  const [hidden, setHidden] = useState<readonly string[]>([]);
  const [selected, setSelected] = useState<readonly string[]>([]);
  const [outcomes, setOutcomes] = useState<Readonly<Record<string, Outcome>>>({});
  const [creating, setCreating] = useState(false);
  const [hovered, setHovered] = useState<string | null>(null);
  const [direction, setDirection] = useState<Direction>('All');
  const [sort, setSort] = useState<Sort>('none');
  const rows = useMemo<Row[]>(
    () => ideas.ideas.map((idea) => ({ key: key(idea), idea, outcome: ideaOutcome(idea) })),
    [ideas.ideas],
  );
  const shorts = rows.some((row) => row.idea.direction === 'Short');
  const visible = sorted(
    rows.filter(
      (row) =>
        !hidden.includes(row.key) &&
        (direction === 'All' || (row.idea.direction ?? 'Long') === direction),
    ),
    sort,
  );
  const chosen = rows.filter(
    (row) => selected.includes(row.key) && !row.idea.taken && !outcomes[row.key],
  );
  const hoveredRow = rows.find((row) => row.key === hovered);
  const preview = hoveredRow ? [hoveredRow.idea] : chosen.map((row) => row.idea);
  const previewKey = preview.map(key).join('|');

  useEffect(() => {
    // The preview is a new array each render; it changes only with the ideas it names.
    onPreview?.(preview);
  }, [previewKey, onPreview]);

  if (rows.length === 0) {
    return (
      <p className="text-sm text-muted">
        Идей нет: ни на одном таймфрейме нет уровней вокруг цены, а последний час стоял на месте.
      </p>
    );
  }

  async function createChosen() {
    setCreating(true);

    for (const { key: rowKey, idea } of chosen) {
      try {
        const trade = await createScanTrade(venue, ideas.symbol, idea);
        setOutcomes((current) => ({ ...current, [rowKey]: { botId: trade.botId } }));
      } catch (error) {
        const message =
          error instanceof ApiError
            ? (createErrors[error.code] ?? error.message)
            : 'Не удалось создать сделку.';
        setOutcomes((current) => ({ ...current, [rowKey]: { error: message } }));
      }
    }

    setSelected([]);
    setCreating(false);
    await queryClient.invalidateQueries({ queryKey: scannerKeys.tradeIdeas(venue, ideas.symbol) });
    await queryClient.invalidateQueries({ queryKey: scannerKeys.trades(venue) });
  }

  function toggle(rowKey: string, on: boolean) {
    setSelected((current) =>
      on ? [...current, rowKey] : current.filter((item) => item !== rowKey),
    );
  }

  function sortBy(next: Sort) {
    setSort((current) => (current === next ? 'none' : next));
  }

  return (
    <div className="space-y-3">
      {!ideas.hasSettings && (
        <p
          role="status"
          className="rounded-md border border-warning-border bg-warning-bg px-3 py-2 text-sm text-warning-text"
        >
          Чтобы выбирать идеи, сохраните риск на сделку и ключ в «Настройках сделок». Без них сумма
          и потеря не считаются.
        </p>
      )}
      {(shorts || hidden.length > 0) && (
        <div className="flex flex-wrap items-center gap-2">
          {shorts && (
            <div role="group" aria-label="Направление" className="flex gap-1">
              {(['All', 'Long', 'Short'] as const).map((item) => (
                <button
                  key={item}
                  type="button"
                  aria-pressed={direction === item}
                  className={filterButton}
                  onClick={() => setDirection(item)}
                >
                  {item === 'All' ? 'Все' : tradeDirectionLabel(item)}
                </button>
              ))}
            </div>
          )}
          {hidden.length > 0 && (
            <Button size="sm" variant="ghost" onClick={() => setHidden([])}>
              Показать скрытые ({hidden.length})
            </Button>
          )}
        </div>
      )}
      <div className="overflow-x-auto">
        <table className="w-full min-w-[56rem] text-right text-sm">
          <thead className="text-xs text-text-secondary">
            <tr className="border-b border-line">
              <th className="w-8 py-2 pr-2 text-left">
                <span className="sr-only">Выбор</span>
              </th>
              <th className="py-2 pr-3 text-left font-normal">Идея</th>
              <th className="px-2 py-2 text-left font-normal">ТФ</th>
              <th className="px-2 py-2 font-normal">Вход</th>
              <th className="px-2 py-2 font-normal">Цель</th>
              <th className="px-2 py-2 font-normal">Стоп</th>
              <SortHeader
                label="Прибыль"
                active={sort === 'profit'}
                onSort={() => sortBy('profit')}
              />
              <th className="px-2 py-2 font-normal">Убыток</th>
              <SortHeader
                label="R"
                title="Прибыль на цели к убытку на стопе, после комиссий"
                active={sort === 'netRewardRisk'}
                onSort={() => sortBy('netRewardRisk')}
              />
              <th className="px-2 py-2 font-normal">Сумма</th>
              <th className="py-2 pl-2">
                <span className="sr-only">Действия</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {visible.map((row) => (
              <IdeaRow
                key={row.key}
                row={row}
                quote={quote}
                checked={selected.includes(row.key)}
                outcome={outcomes[row.key]}
                disabled={creating || !ideas.hasSettings}
                onToggle={(on) => toggle(row.key, on)}
                onHide={() => setHidden((current) => [...current, row.key])}
                onHover={(on) =>
                  setHovered((current) => (on ? row.key : current === row.key ? null : current))
                }
              />
            ))}
          </tbody>
        </table>
      </div>
      {visible.length === 0 && (
        <p className="text-sm text-muted">
          Все идеи скрыты фильтром; они вернутся с новыми ценами.
        </p>
      )}
      <div className="flex flex-wrap items-center gap-3 border-t border-line pt-3">
        <Button
          variant="primary"
          size="sm"
          disabled={chosen.length === 0 || creating || !ideas.hasSettings}
          onClick={() => void createChosen()}
        >
          {creating ? 'Создаю…' : `Создать выбранные (${chosen.length})`}
        </Button>
        {chosen.length > 0 && (
          <span className="text-sm text-text-secondary">
            Сумма{' '}
            {withQuote(
              formatMetric(
                total(chosen, (row) => row.idea.amount),
                2,
              ),
              quote,
            )}
            , прибыль на всех целях{' '}
            {withQuote(
              signed(
                total(chosen, (row) => row.outcome.profit),
                2,
              ),
              quote,
            )}
            , потеря на всех стопах{' '}
            {withQuote(
              formatMetric(
                total(chosen, (row) => row.outcome.loss),
                2,
              ),
              quote,
            )}
          </span>
        )}
        {selected.length > 0 && !creating && (
          <Button size="sm" variant="ghost" onClick={() => setSelected([])}>
            Сбросить
          </Button>
        )}
      </div>
    </div>
  );
}

function SortHeader({
  label,
  title,
  active,
  onSort,
}: {
  label: string;
  title?: string;
  active: boolean;
  onSort: () => void;
}) {
  return (
    <th className="px-2 py-2 font-normal" aria-sort={active ? 'descending' : 'none'} title={title}>
      <button type="button" className={active ? 'text-link' : 'hover:text-text'} onClick={onSort}>
        {label}
        {active ? ' ↓' : ''}
      </button>
    </th>
  );
}

function IdeaRow({
  row,
  quote,
  checked,
  outcome,
  disabled,
  onToggle,
  onHide,
  onHover,
}: {
  row: Row;
  quote: string;
  checked: boolean;
  outcome: Outcome | undefined;
  disabled: boolean;
  onToggle: (on: boolean) => void;
  onHide: () => void;
  onHover: (on: boolean) => void;
}) {
  const { idea, outcome: result } = row;
  const label = tradeScenarioLabels[idea.scenario] ?? idea.scenario;
  const short = idea.direction === 'Short';
  const direction = tradeDirectionLabel(idea.direction);
  const name = `${direction} ${label} ${idea.timeframe}`;
  const locked = idea.taken || outcome !== undefined;
  const hours = idea.entryValidityMinutes / 60;
  const wait = hours >= 1 ? `${hours} ч` : `${idea.entryValidityMinutes} мин`;
  const profitable = (compareDecimals(result.profit ?? '0', '0') ?? 0) > 0;
  const goodRewardRisk = (compareDecimals(result.netRewardRisk ?? '0', '1') ?? -1) >= 0;

  return (
    <tr
      aria-label={`Идея: ${name}`}
      className={`border-b border-line align-top ${checked && !locked ? 'bg-selected' : 'hover:bg-panel-2'}`}
      onMouseEnter={() => onHover(true)}
      onMouseLeave={() => onHover(false)}
      onFocus={() => onHover(true)}
      onBlur={() => onHover(false)}
    >
      <td className="py-2 pr-2 text-left">
        <input
          type="checkbox"
          aria-label={`Выбрать: ${name}`}
          className="mt-0.5"
          checked={checked && !locked}
          disabled={disabled || locked || !idea.amount}
          onChange={(event) => onToggle(event.target.checked)}
        />
      </td>
      <td className="py-2 pr-3 text-left" title={tradeScenarioHint(idea.scenario, idea.direction)}>
        <span className="flex items-center gap-2 whitespace-nowrap">
          <span
            className={`rounded border px-1.5 text-xs font-semibold ${short ? 'border-danger-border text-down' : 'border-line-strong text-up'}`}
          >
            {direction}
          </span>
          <span className="font-medium">{label}</span>
        </span>
        <span className="block text-xs text-muted">
          {short ? 'Продажа' : 'Покупка'} лимиткой, ждёт {wait}
        </span>
      </td>
      <td className="px-2 py-2 text-left text-text-secondary">{idea.timeframe}</td>
      <td className="px-2 py-2 font-mono">{formatMetric(idea.entry, 6)}</td>
      <td className="px-2 py-2 font-mono">
        <span className="text-up">{formatMetric(idea.target, 6)}</span>
        <Sub>{percent(result.targetMove)}</Sub>
      </td>
      <td className="px-2 py-2 font-mono">
        <span className="text-down">{formatMetric(idea.stop, 6)}</span>
        <Sub>{percent(result.stopMove)}</Sub>
      </td>
      <td className="px-2 py-2 font-mono">
        {result.profit ? (
          <>
            <span className={profitable ? 'text-up' : 'text-down'}>
              {withQuote(signed(result.profit, 2), quote)}
            </span>
            <Sub>{percent(result.profitPercent)}</Sub>
          </>
        ) : (
          '—'
        )}
      </td>
      <td className="px-2 py-2 font-mono">
        {result.loss ? (
          <>
            <span className="text-down">
              {withQuote(`-${formatMetric(result.loss, 2)}`, quote)}
            </span>
            <Sub>{`-${formatMetric(result.lossPercent, 2)}%`}</Sub>
          </>
        ) : (
          '—'
        )}
      </td>
      <td className="px-2 py-2 font-mono">
        {result.netRewardRisk ? (
          <span
            className={`rounded-full border px-2 py-0.5 text-xs ${goodRewardRisk ? 'border-line-strong text-up' : 'border-danger-border text-danger-text'}`}
          >
            {formatMetric(result.netRewardRisk, 2)}
          </span>
        ) : (
          '—'
        )}
        <Sub title="Прибыль к риску без комиссий">{`без ком. ${formatMetric(idea.rewardRisk, 2)}`}</Sub>
      </td>
      <td className="px-2 py-2 font-mono">
        {idea.amount ? withQuote(formatMetric(idea.amount, 2), quote) : '—'}
      </td>
      <td className="py-2 pl-2 text-left whitespace-nowrap">
        {outcome && 'botId' in outcome ? (
          <span role="status" className="text-up">
            Создана —{' '}
            <Link
              to="/bots/$botId"
              params={{ botId: outcome.botId }}
              className="text-link hover:underline"
            >
              бот сделки
            </Link>
          </span>
        ) : outcome ? (
          <span role="alert" className="text-danger-text">
            {outcome.error}
          </span>
        ) : idea.taken ? (
          <span className="text-text-secondary">Уже живёт</span>
        ) : (
          <Button size="sm" variant="ghost" onClick={onHide} disabled={disabled}>
            Скрыть
          </Button>
        )}
      </td>
    </tr>
  );
}

function Sub({ children, title }: { children: string; title?: string }) {
  return (
    <span className="block text-xs text-muted" title={title}>
      {children}
    </span>
  );
}

function sorted(rows: Row[], sort: Sort): Row[] {
  if (sort === 'none') {
    return rows;
  }

  const value = (row: Row) => row.outcome[sort];

  return [...rows].sort((left, right) => {
    const a = value(left);
    const b = value(right);

    if (a === null || b === null) {
      return a === null ? (b === null ? 0 : 1) : -1;
    }

    return compareDecimals(b, a) ?? 0;
  });
}

function signed(value: string | null, places: number): string {
  const text = formatMetric(value, places);

  return value !== null && (compareDecimals(value, '0') ?? 0) > 0 ? `+${text}` : text;
}

function percent(value: string | null): string {
  return value === null ? '—' : `${signed(value, 2)}%`;
}

function key(idea: ScanTradeIdea): string {
  return `${idea.timeframe}:${idea.direction ?? 'Long'}:${idea.scenario}:${idea.entry}:${idea.stop}:${idea.target}`;
}

function total(rows: readonly Row[], value: (row: Row) => string | null): string {
  return rows.reduce((sum, row) => addDecimals(sum, value(row) ?? '0') ?? sum, '0');
}
