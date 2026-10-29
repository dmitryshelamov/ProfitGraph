import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { jobStatusLabel } from '@/modules/jobs';
import { DateTime } from '@/shared/ui/DateTime';

import { scanBacktestsQuery } from '../api/scannerApi';

/** The user's latest backtests on the venue, each opening its result. */
export function ScanBacktestList({ venue }: { venue: string }) {
  const backtests = useQuery(scanBacktestsQuery(venue));

  if (!backtests.data || backtests.data.length === 0) {
    return <p className="text-sm text-muted">Проверок на истории ещё не было.</p>;
  }

  return (
    <ul className="space-y-1 text-sm">
      {backtests.data.map((backtest) => (
        <li key={backtest.id} className="flex flex-wrap items-baseline justify-between gap-2">
          <Link
            to="/scanner/backtests/$backtestId"
            params={{ backtestId: backtest.id }}
            className="text-link hover:underline"
          >
            {backtest.presetName}
          </Link>
          <span className="text-muted">
            {backtest.completedAt ? (
              `${backtest.signals ?? 0} сигн.`
            ) : backtest.jobStatus ? (
              jobStatusLabel(backtest.jobStatus)
            ) : (
              <DateTime value={backtest.createdAt} />
            )}
          </span>
        </li>
      ))}
    </ul>
  );
}
