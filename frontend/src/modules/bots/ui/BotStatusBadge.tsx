import { StatusBadge } from '@/shared/ui/StatusBadge';

import { botStatusLabel, botStatusTone } from '../model/botStatus';
import type { Bot } from '../model/types';

export function BotStatusBadge({
  bot,
}: {
  bot: Pick<Bot, 'desiredState' | 'runtimeStatus' | 'statusReason'>;
}) {
  return <StatusBadge tone={botStatusTone(bot)}>{botStatusLabel(bot)}</StatusBadge>;
}
