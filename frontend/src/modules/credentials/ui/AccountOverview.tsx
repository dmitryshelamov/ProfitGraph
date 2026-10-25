import { useQuery } from '@tanstack/react-query';

import { DateTime } from '@/shared/ui/DateTime';

import { accountHistoryQuery } from '../api/credentialsApi';

const failureLabels: Record<string, string> = {
  venue_unavailable: 'биржа не отвечает',
  venue_rejected: 'биржа отклонила запрос',
  unsupported_response: 'непонятный ответ биржи',
  credential_unavailable: 'ключ недоступен',
  venue_unsupported: 'биржа не поддерживается',
};

/**
 * The value of a key's exchange account, read every five minutes while a bot uses the key. It includes deposits,
 * withdrawals and price moves, so it is not the bots' profit.
 */
export function AccountOverview({ credentialId }: { credentialId: string }) {
  const history = useQuery(accountHistoryQuery(credentialId));

  if (history.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить обзор счёта.
      </p>
    );
  }

  if (!history.data) {
    return <p className="text-muted">Загрузка…</p>;
  }

  const { snapshots, lastSuccessAt, failureReason, nextAttemptAt } = history.data;
  const latest = snapshots.at(-1);

  return (
    <div className="space-y-3 text-sm">
      {latest ? (
        <>
          <p>
            Стоимость счёта: <span className="font-mono">{latest.totalValueUsd}</span> USD
            {latest.availableValueUsd && (
              <>
                , доступно <span className="font-mono">{latest.availableValueUsd}</span> USD
              </>
            )}{' '}
            (<DateTime value={latest.capturedAt} />)
          </p>
          <ul className="flex flex-wrap gap-x-4 gap-y-1" aria-label="Монеты счёта">
            {latest.coins.map((coin) => (
              <li key={coin.coin}>
                {coin.coin}: <span className="font-mono">{coin.balance}</span>
                {coin.usdValue && <span className="text-muted"> ≈ {coin.usdValue} USD</span>}
              </li>
            ))}
          </ul>
          <p className="text-muted">Снимков за неделю: {snapshots.length}</p>
        </>
      ) : (
        <p className="text-muted">Снимков пока нет: счёт читается, когда ключ использует бот.</p>
      )}
      {failureReason && (
        <p className="text-danger-text">
          Последнее чтение не удалось: {failureLabels[failureReason] ?? failureReason}
          {lastSuccessAt && (
            <>
              ; последний успешный снимок <DateTime value={lastSuccessAt} />
            </>
          )}
          .
        </p>
      )}
      {nextAttemptAt && (
        <p className="text-muted">
          Следующее чтение после <DateTime value={nextAttemptAt} />
        </p>
      )}
    </div>
  );
}
