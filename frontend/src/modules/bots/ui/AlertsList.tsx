import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { Button } from '@/shared/ui/Button';
import { DateTime } from '@/shared/ui/DateTime';

import { acknowledgeAlert, botAlertsQuery, botKeys } from '../api/botsApi';
import { alertLabel, reasonLabel } from '../model/botStatus';

export function AlertsList({ botId }: { botId: string }) {
  const queryClient = useQueryClient();
  const alerts = useQuery(botAlertsQuery(botId));
  const acknowledge = useMutation({
    mutationFn: acknowledgeAlert,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: botKeys.alerts(botId) }),
  });

  if (alerts.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить алерты.
      </p>
    );
  }

  if (!alerts.data || alerts.data.length === 0) {
    return <p className="text-muted">{alerts.isPending ? 'Загрузка…' : 'Алертов нет.'}</p>;
  }

  return (
    <ul className="space-y-2" aria-label="Алерты бота">
      {alerts.data.map((alert) => (
        <li
          key={alert.id}
          className="flex flex-wrap items-center justify-between gap-2 border-t border-line pt-2 text-sm"
        >
          <span className={alert.acknowledgedAt ? 'text-muted' : ''}>
            <DateTime value={alert.createdAt} /> — {alertLabel(alert.kind)}
            {alert.reason && `: ${reasonLabel(alert.reason)}`}
          </span>
          {!alert.acknowledgedAt && (
            <Button
              size="sm"
              variant="ghost"
              disabled={acknowledge.isPending}
              onClick={() => acknowledge.mutate(alert.id)}
            >
              Прочитано
            </Button>
          )}
        </li>
      ))}
    </ul>
  );
}
