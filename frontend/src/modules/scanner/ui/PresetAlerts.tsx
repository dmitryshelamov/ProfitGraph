import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { telegramLinkQuery } from '@/modules/notifications';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';

import { scannerKeys, setScanPresetAlerts } from '../api/scannerApi';
import type { ScanPreset } from '../model/types';

/**
 * The Telegram alert switch of a saved preset (ADR 0059): off by default; when on, a pair that enters the preset's result is sent
 * to the linked chat at most once an hour. A built-in or unsaved preset is saved first.
 */
export function PresetAlerts({ preset }: { preset: ScanPreset | undefined }) {
  const queryClient = useQueryClient();
  const link = useQuery(telegramLinkQuery());
  const toggle = useMutation({
    mutationFn: ({ presetId, enabled }: { presetId: string; enabled: boolean }) =>
      setScanPresetAlerts(presetId, enabled),
    onSuccess: (saved) =>
      queryClient.invalidateQueries({ queryKey: scannerKeys.presets(saved.venue) }),
  });

  if (!preset?.id || preset.builtIn) {
    return (
      <p className="text-sm text-muted">
        Алерты в Telegram включаются у сохранённого пресета: сохраните его или копию встроенного.
      </p>
    );
  }

  const presetId = preset.id;
  const enabled = toggle.isPending ? toggle.variables.enabled : preset.alertsEnabled;
  const chat = link.data;

  return (
    <div className="space-y-1">
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={enabled}
          disabled={toggle.isPending}
          onChange={(event) => toggle.mutate({ presetId, enabled: event.target.checked })}
        />
        Алерты в Telegram
      </label>
      <p className="text-xs text-muted">
        Новая пара в результате пресета приходит в чат не чаще раза в час; кнопка «Отключить» в
        сообщении выключает алерты.
      </p>
      {enabled && chat && (!chat.linked || chat.blocked) && (
        <p role="status" className="text-sm text-warning-text">
          {chat.blocked ? 'Бот заблокирован в Telegram' : 'Чат Telegram не привязан'} — алерты не
          придут.{' '}
          <Link to="/profile" className="text-link hover:underline">
            Профиль
          </Link>
        </p>
      )}
      {toggle.isError && (
        <ApiErrorMessage error={toggle.error} fallback="Не удалось переключить алерты." />
      )}
    </div>
  );
}
