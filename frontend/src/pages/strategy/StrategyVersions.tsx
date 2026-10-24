import { useQuery } from '@tanstack/react-query';

import { strategyVersionsQuery } from '@/modules/strategies';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';

export function StrategyVersions({
  strategyId,
  currentVersion,
}: {
  strategyId: string;
  currentVersion: number;
}) {
  const versions = useQuery(strategyVersionsQuery(strategyId));

  return (
    <Card title="Версии">
      {versions.isPending && <p role="status">Загрузка версий…</p>}
      {versions.isError && (
        <p role="alert" className="text-danger-text">
          Не удалось загрузить версии: {versions.error.message}
        </p>
      )}
      {versions.data && (
        <ol className="space-y-2 text-sm">
          {versions.data.map((version) => (
            <li key={version.id} className="flex justify-between gap-3">
              <span>
                v{version.number}
                {version.number === currentVersion && (
                  <span className="ml-2 text-muted">текущая</span>
                )}
              </span>
              <span className="text-muted">
                <DateTime value={version.createdAt} />
              </span>
            </li>
          ))}
        </ol>
      )}
    </Card>
  );
}
