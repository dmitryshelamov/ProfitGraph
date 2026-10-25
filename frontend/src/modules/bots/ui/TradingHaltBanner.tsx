import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useCurrentUser } from '@/modules/accounts';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';

import { botKeys, haltTrading, resumeTrading, tradingHaltQuery } from '../api/botsApi';

/**
 * Shows the global halt to everyone; the Owner halts, which kills every bot that should run, or resumes, which starts
 * none.
 */
export function TradingHaltBanner() {
  const user = useCurrentUser();
  const queryClient = useQueryClient();
  const halt = useQuery(tradingHaltQuery());
  const change = useMutation({
    mutationFn: (halted: boolean) => (halted ? resumeTrading() : haltTrading()),
    onSuccess: async (updated) => {
      queryClient.setQueryData(botKeys.tradingHalt, updated);
      await queryClient.invalidateQueries({ queryKey: botKeys.all });
    },
  });
  const halted = halt.data?.isHalted ?? false;
  const owner = user?.role === 'Owner';

  if (!halted && !owner) {
    return null;
  }

  return (
    <div
      role={halted ? 'alert' : undefined}
      className={`mb-6 flex flex-wrap items-center justify-between gap-3 rounded-lg border px-4 py-3 text-sm ${halted ? 'border-danger-text text-danger-text' : 'border-line'}`}
    >
      <span>
        {halted
          ? 'Торговля остановлена владельцем: боты не запускаются и не отправляют заявки.'
          : 'Торговля разрешена.'}
      </span>
      {owner && (
        <Button
          size="sm"
          disabled={change.isPending || halt.isPending}
          onClick={() => change.mutate(halted)}
        >
          {halted ? 'Возобновить торговлю' : 'Остановить всю торговлю'}
        </Button>
      )}
      {change.isError && (
        <ApiErrorMessage error={change.error} fallback="Не удалось изменить остановку торговли." />
      )}
    </div>
  );
}
