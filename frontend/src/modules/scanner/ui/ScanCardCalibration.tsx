import { Link } from '@tanstack/react-router';

import { compareDecimals, fractionToPercent } from '@/shared/lib/decimal';

import { formatMetric } from '../model/format';
import type { ScanBacktest } from '../model/types';

type Calibration = NonNullable<NonNullable<ScanBacktest['summary']>['calibration']>;

function percent(fraction: string): string {
  return `${formatMetric(fractionToPercent(fraction), 0)}%`;
}

/**
 * How well the odds of the owner's other backtests over other periods predicted this backtest's cards (ADR 0073): the share that
 * reached the target against the mean chance, the Brier score of the chances against the random walk's 1 / (1 + R) and the bands
 * before ADR 0073, and the cards by tenths of the chance.
 */
export function ScanCardCalibration({ calibration }: { calibration: Calibration }) {
  const better = (compareDecimals(calibration.brier, calibration.geometryBrier) ?? 0) < 0;

  return (
    <div className="space-y-3">
      <p className="text-sm text-text-secondary">
        Шансы карточек по{' '}
        {calibration.sources.map((source, index) => (
          <span key={source}>
            {index > 0 && ', '}
            <Link
              to="/scanner/backtests/$backtestId"
              params={{ backtestId: source }}
              className="text-link hover:underline"
            >
              {`проверке ${index + 1}`}
            </Link>
          </span>
        ))}{' '}
        за другие периоды, приложенные к {calibration.cards} карточкам этой: ожидали цель в{' '}
        {percent(calibration.predicted)}, дошли до неё {percent(calibration.observed)}. Оценка
        Брайера (средний квадрат ошибки, меньше — лучше): {formatMetric(calibration.brier, 4)} у
        шансов, {formatMetric(calibration.geometryBrier, 4)} у одного 1 / (1 + R),{' '}
        {formatMetric(calibration.bandsBrier, 4)} у прежних полос.{' '}
        {better
          ? 'Шансы предсказали лучше случайного блуждания.'
          : 'Шансы не предсказали лучше случайного блуждания.'}
      </p>
      <div className="overflow-x-auto">
        <table className="w-full text-right text-sm" aria-label="Калибровка шансов">
          <thead className="text-text-secondary">
            <tr>
              <th className="py-2 pr-3 text-left">Шанс</th>
              <th className="py-2 pl-3">Карточек</th>
              <th className="py-2 pl-3">Ожидали</th>
              <th className="py-2 pl-3">Дошли до цели</th>
            </tr>
          </thead>
          <tbody>
            {calibration.bins.map((bin) => (
              <tr key={bin.lower} className="border-t border-line">
                <td className="py-1.5 pr-3 text-left">{`${percent(bin.lower)}–${percent(bin.upper)}`}</td>
                <td className="py-1.5 pl-3 font-mono">{bin.cards}</td>
                <td className="py-1.5 pl-3 font-mono">{percent(bin.predicted)}</td>
                <td className="py-1.5 pl-3 font-mono">{percent(bin.observed)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
