import { PriceEquityChart } from '@/modules/market-chart';
import { Card } from '@/shared/ui/Card';

import type { RunProgress } from '../model/useRunProgress';
import { Amount } from './Amount';

const connections: Record<RunProgress['connection'], string> = {
  connecting: 'Подключение…',
  live: 'В реальном времени',
  reconnecting: 'Переподключение…',
  offline: 'Нет связи, обновления по запросу',
};

/** Candles a running replay has stored so far, with the account after the latest one. */
export function RunLive({ progress, quote }: { progress: RunProgress; quote: string }) {
  const latest = progress.observations.at(-1);

  return (
    <Card
      title="Ход прогона"
      actions={<span className="text-sm text-muted">{connections[progress.connection]}</span>}
    >
      {latest ? (
        <div className="space-y-4">
          <dl className="grid gap-4 text-sm sm:grid-cols-3">
            <div>
              <dt className="text-muted">Свеча</dt>
              <dd className="font-mono">{latest.sequence}</dd>
            </div>
            <div>
              <dt className="text-muted">Капитал</dt>
              <dd>
                <Amount value={latest.equity} suffix={quote} />
              </dd>
            </div>
            <div>
              <dt className="text-muted">Прибыль</dt>
              <dd>
                <Amount value={latest.totalPnl} suffix={quote} />
              </dd>
            </div>
          </dl>
          <PriceEquityChart points={progress.observations} label="График прогона" />
        </div>
      ) : (
        <p className="text-muted">Свечи появятся, когда начнётся проигрывание истории.</p>
      )}
    </Card>
  );
}
