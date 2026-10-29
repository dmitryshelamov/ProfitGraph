import { formatMetric } from '../model/format';
import type { ScanLevels } from '../model/types';

/**
 * The nearest support and resistance on the chart's timeframe, their distances from the price and the reward-to-risk of a
 * long from it, to judge an entry on that timeframe; the scanner's own metrics stay on hours.
 */
export function ScanTimeframeLevels({ levels }: { levels: ScanLevels }) {
  const rows = [
    { label: 'Поддержка', value: formatMetric(levels.support, null) },
    { label: 'До подд., %', value: formatMetric(levels.distanceToSupport, 2) },
    { label: 'Сопротивление', value: formatMetric(levels.resistance, null) },
    { label: 'До сопр., %', value: formatMetric(levels.distanceToResistance, 2) },
    { label: 'R:R', value: formatMetric(levels.rewardRisk, 2) },
  ];

  return (
    <div className="space-y-3">
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm sm:grid-cols-3 lg:grid-cols-1">
        {rows.map((row) => (
          <div key={row.label} className="flex justify-between gap-3">
            <dt className="text-text-secondary">{row.label}</dt>
            <dd className="font-mono">{row.value}</dd>
          </div>
        ))}
      </dl>
      <p className="text-xs text-muted">
        {levels.levels.length === 0
          ? `Мало закрытых свечей ${levels.timeframe}: уровни строятся минимум по 24.`
          : `По ${levels.candleCount} закрытым свечам ${levels.timeframe}; R:R — для лонга от текущей цены.`}
      </p>
      {levels.status === 'Unavailable' && (
        <p role="status" className="text-xs text-text-secondary">
          Площадка сейчас не отвечает: уровни по сохранённым свечам.
        </p>
      )}
    </div>
  );
}
