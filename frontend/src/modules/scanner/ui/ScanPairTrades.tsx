import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { StatusBadge } from '@/shared/ui/StatusBadge';

import { scanPairTradesQuery } from '../api/scannerApi';
import { formatMetric, withQuote } from '../model/format';
import {
  tradeDirectionLabel,
  tradeFailure,
  tradeScenarioLabels,
  tradeStatus,
} from '../model/trades';
import { useScanQuote } from './useScanQuote';

/** The owner's trades from cards on the pair (ADR 0060), newest first, each with its bot. */
export function ScanPairTrades({ venue, symbol }: { venue: string; symbol: string }) {
  const trades = useQuery(scanPairTradesQuery(venue, symbol));
  const quote = useScanQuote(venue);

  if (!trades.data || trades.data.length === 0) {
    return null;
  }

  return (
    <Card title="Сделки по паре">
      <div className="overflow-x-auto">
        <table className="w-full text-right text-sm">
          <thead className="text-text-secondary">
            <tr>
              <th className="py-2 pr-3 text-left">Создана</th>
              <th className="px-3 py-2 text-left">Сценарий</th>
              <th className="px-3 py-2">Вход</th>
              <th className="px-3 py-2">Стоп</th>
              <th className="px-3 py-2">Цель</th>
              <th className="px-3 py-2">Сумма</th>
              <th className="px-3 py-2 text-left">Статус</th>
              <th className="px-3 py-2">Результат</th>
              <th className="py-2 pl-3" />
            </tr>
          </thead>
          <tbody>
            {trades.data.map((trade) => {
              const status = tradeStatus(trade.status);
              const failure = tradeFailure(trade.failureCode);

              return (
                <tr key={trade.id} className="border-t border-line">
                  <td className="py-2 pr-3 text-left">
                    <DateTime value={trade.createdAt} />
                  </td>
                  <td className="px-3 py-2 text-left">
                    {trade.direction === 'Short' && `${tradeDirectionLabel(trade.direction)} · `}
                    {tradeScenarioLabels[trade.scenario] ?? trade.scenario} {trade.levelsTimeframe}
                  </td>
                  <td className="px-3 py-2">{formatMetric(trade.entry, 6)}</td>
                  <td className="px-3 py-2">{formatMetric(trade.stop, 6)}</td>
                  <td className="px-3 py-2">{formatMetric(trade.target, 6)}</td>
                  <td className="px-3 py-2">{formatMetric(trade.amount, 2)}</td>
                  <td className="px-3 py-2 text-left">
                    <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
                    {failure && <span className="ml-2 text-xs text-muted">{failure}</span>}
                  </td>
                  <td className="px-3 py-2">
                    {trade.realizedPnl ? withQuote(formatMetric(trade.realizedPnl, 2), quote) : '—'}
                  </td>
                  <td className="py-2 pl-3">
                    <Link
                      to="/bots/$botId"
                      params={{ botId: trade.botId }}
                      className="text-link hover:underline"
                    >
                      Бот
                    </Link>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Card>
  );
}
