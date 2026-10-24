import {
  parameterReadableName,
  parameterValueDisplay,
  type StrategyDefinition,
} from '@/modules/strategies';
import { Card } from '@/shared/ui/Card';

export function StrategyParameters({ definition }: { definition: StrategyDefinition }) {
  return (
    <Card title="Параметры">
      {definition.parameters.length === 0 ? (
        <p className="text-muted">У стратегии нет параметров.</p>
      ) : (
        <dl className="divide-y divide-line">
          {definition.parameters.map((parameter) => (
            <div key={parameter.name} className="flex items-baseline justify-between gap-4 py-2">
              <dt>
                {parameterReadableName(parameter.name)}
                <span className="ml-2 text-xs text-muted">{parameter.name}</span>
              </dt>
              <dd className="font-mono" title={parameter.defaultValue}>
                {parameterValueDisplay(parameter.defaultValue, parameter.displayUnit)}
              </dd>
            </div>
          ))}
        </dl>
      )}
    </Card>
  );
}
