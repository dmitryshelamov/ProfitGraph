import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { DateTime } from '@/shared/ui/DateTime';
import { StatusBadge } from '@/shared/ui/StatusBadge';

import { scanPairTradesQuery } from '../api/scannerApi';
import { formatMetric } from '../model/format';
import { tradeFailure, tradeScenarioLabels, tradeStatus } from '../model/trades';

/** The owner's trades from cards on the pair (ADR 0060), newest first, each with its bot. */
export function ScanPairTrades({ venue, symbol }: { venue: string; symbol: string }) {
  const trades = useQuery(scanPairTradesQuery(venue, symbol));

  if (!trades.data || trades.data.length === 0) {
    return null;
  }

  return (
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
                  {tradeScenarioLabels[trade.scenario] ?? trade.scenario}
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
                  {trade.realizedPnl ? `${formatMetric(trade.realizedPnl, 2)} USDT` : '—'}
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
  );
}
