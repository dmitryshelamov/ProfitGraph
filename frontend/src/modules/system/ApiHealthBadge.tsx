import { StatusBadge, type StatusTone } from '@/shared/ui/StatusBadge';

import { type ApiHealth, useApiHealth } from './useApiHealth';

const states: Record<ApiHealth, { tone: StatusTone; label: string }> = {
  checking: { tone: 'neutral', label: 'Проверка…' },
  healthy: { tone: 'success', label: 'API доступен' },
  unavailable: { tone: 'danger', label: 'API недоступен' },
};

export function ApiHealthBadge() {
  const { tone, label } = states[useApiHealth()];

  return <StatusBadge tone={tone}>{label}</StatusBadge>;
}
