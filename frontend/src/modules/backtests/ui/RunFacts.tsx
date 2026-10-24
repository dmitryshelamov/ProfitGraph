import { type InfiniteData, useInfiniteQuery } from '@tanstack/react-query';
import { type ReactNode, useState } from 'react';

import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';

import { backtestDiagnosticsQuery, backtestFillsQuery, backtestOrdersQuery } from '../api/runsApi';
import type { BacktestDiagnostic, BacktestLedgerEntry } from '../model/types';
import { Amount } from './Amount';

type Tab = 'orders' | 'fills' | 'diagnostics';

const tabs: readonly [Tab, string][] = [
  ['orders', 'Ордера'],
  ['fills', 'Сделки'],
  ['diagnostics', 'Диагностика'],
];

const sides: Record<string, string> = { Buy: 'Покупка', Sell: 'Продажа' };
const types: Record<string, string> = { Market: 'Рыночный', Limit: 'Лимитный' };

function Table({
  head,
  rows,
  empty,
}: {
  head: readonly string[];
  rows: ReactNode[];
  empty: string;
}) {
  if (rows.length === 0) {
    return <p className="text-muted">{empty}</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-muted">
            {head.map((label) => (
              <th key={label} className="py-2 pr-4 font-normal">
                {label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">{rows}</tbody>
      </table>
    </div>
  );
}

function More<T>({
  query,
}: {
  query: {
    hasNextPage: boolean;
    isFetchingNextPage: boolean;
    fetchNextPage: () => unknown;
    data?: InfiniteData<T>;
  };
}) {
  return query.hasNextPage ? (
    <Button
      size="sm"
      className="mt-3"
      disabled={query.isFetchingNextPage}
      onClick={() => void query.fetchNextPage()}
    >
      Показать ещё
    </Button>
  ) : null;
}

function Orders({ runId }: { runId: string }) {
  const query = useInfiniteQuery(backtestOrdersQuery(runId));
  const entries: BacktestLedgerEntry[] = query.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <>
      <Table
        head={[
          'Ордер',
          'Метка',
          'Сторона',
          'Тип',
          'Цена',
          'Количество',
          'Бюджет',
          'Состояние',
          'Свеча',
        ]}
        empty={query.isPending ? 'Загрузка…' : 'Ордеров не было.'}
        rows={entries.map((entry) => (
          <tr key={entry.order!.id}>
            <td className="py-2 pr-4 font-mono">{entry.order!.id}</td>
            <td className="py-2 pr-4">{entry.order!.tag ?? '—'}</td>
            <td className="py-2 pr-4">{sides[entry.order!.side] ?? entry.order!.side}</td>
            <td className="py-2 pr-4">{types[entry.order!.type] ?? entry.order!.type}</td>
            <td className="py-2 pr-4">
              <Amount value={entry.order!.limitPrice} places={8} />
            </td>
            <td className="py-2 pr-4">
              <Amount value={entry.order!.quantity} places={8} />
            </td>
            <td className="py-2 pr-4">
              <Amount value={entry.order!.quoteBudget} />
            </td>
            <td className="py-2 pr-4">{entry.order!.status}</td>
            <td className="py-2 pr-4">
              <DateTime value={entry.candleOpenTime} />
            </td>
          </tr>
        ))}
      />
      <More query={query} />
    </>
  );
}

function Fills({ runId }: { runId: string }) {
  const query = useInfiniteQuery(backtestFillsQuery(runId));
  const entries: BacktestLedgerEntry[] = query.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <>
      <Table
        head={[
          'Свеча',
          'Ордер',
          'Сторона',
          'Цена',
          'Количество',
          'Комиссия',
          'Реализованная прибыль',
        ]}
        empty={query.isPending ? 'Загрузка…' : 'Сделок не было.'}
        rows={entries.map((entry) => (
          <tr key={entry.sequence}>
            <td className="py-2 pr-4">
              <DateTime value={entry.candleOpenTime} />
            </td>
            <td className="py-2 pr-4 font-mono">{entry.fill!.orderId}</td>
            <td className="py-2 pr-4">{sides[entry.fill!.side] ?? entry.fill!.side}</td>
            <td className="py-2 pr-4">
              <Amount value={entry.fill!.price} places={8} />
            </td>
            <td className="py-2 pr-4">
              <Amount value={entry.fill!.grossBase} places={8} />
            </td>
            <td className="py-2 pr-4">
              <Amount
                value={entry.fill!.fee}
                places={8}
                suffix={entry.fill!.feeAsset === 'Base' ? ' в базовой' : ' в котировке'}
              />
            </td>
            <td className="py-2 pr-4">
              <Amount value={entry.fill!.realizedPnl} />
            </td>
          </tr>
        ))}
      />
      <More query={query} />
    </>
  );
}

function Diagnostics({ runId }: { runId: string }) {
  const query = useInfiniteQuery(backtestDiagnosticsQuery(runId));
  const items: BacktestDiagnostic[] = query.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <>
      <Table
        head={['№', 'Код', 'Сообщение', 'Свеча', 'Блок']}
        empty={query.isPending ? 'Загрузка…' : 'Диагностики нет.'}
        rows={items.map((item) => (
          <tr key={item.sequence}>
            <td className="py-2 pr-4 font-mono">{item.sequence}</td>
            <td className="py-2 pr-4 font-mono">{item.code}</td>
            <td className="py-2 pr-4">{item.message}</td>
            <td className="py-2 pr-4 font-mono">{item.candleIndex ?? '—'}</td>
            <td className="py-2 pr-4 font-mono">{item.nodeId ?? '—'}</td>
          </tr>
        ))}
      />
      <More query={query} />
    </>
  );
}

/** Stored orders (latest state of each), fills and diagnostics of a run, page by page. */
export function RunFacts({ runId }: { runId: string }) {
  const [tab, setTab] = useState<Tab>('fills');

  return (
    <Card
      title="Факты прогона"
      actions={
        <div role="tablist" aria-label="Факты прогона" className="flex gap-1">
          {tabs.map(([value, label]) => (
            <Button
              key={value}
              size="sm"
              variant={tab === value ? 'secondary' : 'ghost'}
              role="tab"
              aria-selected={tab === value}
              onClick={() => setTab(value)}
            >
              {label}
            </Button>
          ))}
        </div>
      }
    >
      <div role="tabpanel" aria-label={tabs.find(([value]) => value === tab)![1]}>
        {tab === 'orders' && <Orders runId={runId} />}
        {tab === 'fills' && <Fills runId={runId} />}
        {tab === 'diagnostics' && <Diagnostics runId={runId} />}
      </div>
    </Card>
  );
}
