import { useQuery } from '@tanstack/react-query';

import { strategyValidationQuery } from '@/modules/strategies';
import { Card } from '@/shared/ui/Card';
import { StatusBadge } from '@/shared/ui/StatusBadge';

export function StrategyValidationCard({
  strategyId,
  version,
}: {
  strategyId: string;
  version: number;
}) {
  const validation = useQuery(strategyValidationQuery(strategyId, version));

  return (
    <Card title="Схема">
      {validation.isPending && <p role="status">Проверка схемы…</p>}
      {validation.isError && (
        <p role="alert" className="text-danger-text">
          Не удалось проверить схему: {validation.error.message}
        </p>
      )}
      {validation.data &&
        (validation.data.isValid ? (
          <StatusBadge tone="success">Версия {version} без ошибок</StatusBadge>
        ) : (
          <div className="space-y-3">
            <StatusBadge tone="danger">
              Ошибок в версии {version}: {validation.data.diagnostics.length}
            </StatusBadge>
            <ul className="space-y-2 text-sm">
              {validation.data.diagnostics.map((diagnostic, index) => (
                <li
                  key={`${diagnostic.code}-${index}`}
                  className="rounded-md border border-line bg-panel-2 px-3 py-2"
                >
                  {diagnostic.message}
                  {diagnostic.blockId && (
                    <span className="text-muted"> · блок {diagnostic.blockId}</span>
                  )}
                </li>
              ))}
            </ul>
          </div>
        ))}
    </Card>
  );
}
