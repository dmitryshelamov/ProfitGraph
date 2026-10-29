import { formatMetric } from '../model/format';
import type { ScanLevels } from '../model/types';

/**
 * The nearest support and resistance on the chart's timeframe in one line under the chart, with their distances from the price and
 * the reward-to-risk of a long from it, to judge an entry on that timeframe; the scanner's own metrics stay on hours.
 */
export function ScanTimeframeLevels({ levels }: { levels: ScanLevels }) {
  const items = [
    {
      label: 'Поддержка',
      value: formatMetric(levels.support, null),
      distance: levels.distanceToSupport,
      tone: 'text-up',
    },
    {
      label: 'Сопротивление',
      value: formatMetric(levels.resistance, null),
      distance: levels.distanceToResistance,
      tone: 'text-down',
    },
  ];

  return (
    <section aria-label={`Уровни ${levels.timeframe}`} className="space-y-1">
      <div className="flex flex-wrap items-baseline gap-x-6 gap-y-1 text-sm">
        <h3 className="font-semibold">Уровни {levels.timeframe}</h3>
        <dl className="contents">
          {items.map((item) => (
            <div key={item.label} className="flex items-baseline gap-2">
              <dt className="text-text-secondary">{item.label}</dt>
              <dd className={`font-mono ${item.tone}`}>{item.value}</dd>
              {item.distance !== null && (
                <dd className="font-mono text-xs text-muted">{formatMetric(item.distance, 2)}%</dd>
              )}
            </div>
          ))}
          <div
            className="flex items-baseline gap-2"
            title="Прибыль к риску лонга от текущей цены, без комиссий"
          >
            <dt className="text-text-secondary">R:R</dt>
            <dd className="font-mono">{formatMetric(levels.rewardRisk, 2)}</dd>
          </div>
        </dl>
      </div>
      <p className="text-xs text-muted">
        {levels.levels.length === 0
          ? `Мало закрытых свечей ${levels.timeframe}: уровни строятся минимум по 24.`
          : `По ${levels.candleCount} закрытым свечам ${levels.timeframe}; пунктир на графике.`}
        {levels.status === 'Unavailable' && ' Площадка не отвечает: уровни по сохранённым свечам.'}
      </p>
    </section>
  );
}
