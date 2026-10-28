import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import {
  accountReadFailureLabel,
  protectionReasons,
  steamGuardStatuses,
} from '@/modules/credentials';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';

import { botAccountQuery } from '../api/botsApi';
import {
  accessLevelLabel,
  accountIdentifier,
  accountKindLabel,
  ageLabel,
  isStale,
} from '../model/botAccount';
import { withAsset } from '../model/botDisplay';
import type { BotAccount } from '../model/types';

function Identity({ account }: { account: BotAccount }) {
  const identifier = accountIdentifier(account);
  const identity = account.identity;
  const facts = [
    identity?.kind ? accountKindLabel(identity.kind) : null,
    identity?.accessLevel ? accessLevelLabel(identity.accessLevel) : null,
  ].filter((fact): fact is string => fact !== null);

  return (
    <p className="text-sm">
      {identifier ? (
        <>
          {identifier.label} <span className="font-mono">{identifier.value}</span>
        </>
      ) : (
        <span className="text-muted">Аккаунт ещё не прочитан</span>
      )}
      {identity?.name && <> · «{identity.name}»</>}
      {facts.length > 0 && <span className="text-text-secondary"> · {facts.join(', ')}</span>}
    </p>
  );
}

function Details({ account, now }: { account: BotAccount; now: number }) {
  const { snapshot, identity } = account;

  return (
    <details className="text-sm">
      <summary className="cursor-pointer text-text-secondary hover:text-text">Подробнее</summary>
      <dl className="mt-3 grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2">
        <dt className="text-text-secondary">Монеты</dt>
        <dd>
          {snapshot && snapshot.coins.length > 0 ? (
            <ul className="flex flex-wrap gap-x-4 gap-y-1" aria-label="Монеты счёта">
              {snapshot.coins.map((coin) => (
                <li key={coin.coin}>
                  {coin.coin}: <span className="font-mono">{coin.balance}</span>
                  {coin.usdValue && <span className="text-muted"> ≈ {coin.usdValue} USD</span>}
                </li>
              ))}
            </ul>
          ) : (
            '—'
          )}
        </dd>
        {identity && (
          <>
            <dt className="text-text-secondary">Права ключа</dt>
            <dd>
              {identity.permissions.length > 0 ? (
                <span className="font-mono">{identity.permissions.join(', ')}</span>
              ) : (
                '—'
              )}
            </dd>
            <dt className="text-text-secondary">Ключ действует до</dt>
            <dd>{identity.expiresAt ? <DateTime value={identity.expiresAt} /> : 'без срока'}</dd>
            <dt className="text-text-secondary">Описание аккаунта</dt>
            <dd>
              прочитано <DateTime value={identity.readAt} />
            </dd>
          </>
        )}
        {account.identityFailureReason && (
          <>
            <dt className="text-text-secondary">Чтение описания</dt>
            <dd className="text-danger-text">
              не удалось: {accountReadFailureLabel(account.identityFailureReason)}
            </dd>
          </>
        )}
        {account.venueStatus && (
          <>
            <dt className="text-text-secondary">Статус на площадке</dt>
            <dd>
              {steamGuardStatuses[account.venueStatus.status] ?? account.venueStatus.status}
              {account.venueStatus.nextCheckAt && (
                <span className="text-muted">
                  {' '}
                  (проверка после <DateTime value={account.venueStatus.nextCheckAt} />)
                </span>
              )}
            </dd>
          </>
        )}
        <dt className="text-text-secondary">Другие запущенные боты</dt>
        <dd>
          {account.otherRunningBots.length > 0 ? (
            <ul className="space-y-1" aria-label="Другие запущенные боты ключа">
              {account.otherRunningBots.map((other) => (
                <li key={other.botId}>
                  <Link
                    to="/bots/$botId"
                    params={{ botId: other.botId }}
                    className="text-link hover:underline"
                  >
                    {other.name ?? other.symbol}
                  </Link>
                  : капитал <span className="font-mono">{other.allocationCap}</span>
                </li>
              ))}
            </ul>
          ) : (
            'нет'
          )}
        </dd>
        <dt className="text-text-secondary">Чтение счёта</dt>
        <dd>
          {account.lastAttemptAt ? (
            <>
              последнее <DateTime value={account.lastAttemptAt} />
              {account.nextAttemptAt && (
                <>
                  , следующее после <DateTime value={account.nextAttemptAt} />
                </>
              )}
              <span className="text-muted"> ({ageLabel(account.lastAttemptAt, now)})</span>
            </>
          ) : (
            'ещё не было'
          )}
        </dd>
      </dl>
    </details>
  );
}

/**
 * The exchange account the bot trades on, first on the bot page (ADR 0049): which account and key, the account's value
 * and what is available with the snapshot's age, the bot's capital next to the other running bots of the key, and why the
 * last read failed or the venue holds the account back. Rarely needed details sit behind a disclosure. Everything comes
 * from the server's stored data; nothing here reads the venue.
 */
export function BotAccountCard({
  botId,
  quoteAsset,
}: {
  botId: string;
  quoteAsset: string | null;
}) {
  const account = useQuery(botAccountQuery(botId));
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 30_000);

    return () => window.clearInterval(timer);
  }, []);

  return (
    <Card>
      <section aria-label="Счёт на бирже" className="space-y-3">
        {account.isError ? (
          <p className="text-sm text-muted">Сводка счёта недоступна.</p>
        ) : !account.data ? (
          <p className="text-sm text-muted">Загрузка…</p>
        ) : (
          <AccountSummary account={account.data} quoteAsset={quoteAsset} now={now} />
        )}
      </section>
    </Card>
  );
}

function AccountSummary({
  account,
  quoteAsset,
  now,
}: {
  account: BotAccount;
  quoteAsset: string | null;
  now: number;
}) {
  const { snapshot, protection } = account;
  const others = account.otherRunningBots.length;

  return (
    <>
      <header className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <h2 className="font-semibold">Счёт</h2>
        <span className="text-sm text-muted">
          ключ «{account.credentialLabel}» <span className="font-mono">…{account.apiKeyLast4}</span>
        </span>
      </header>
      <Identity account={account} />
      {snapshot ? (
        <div className="flex flex-wrap items-baseline gap-x-6 gap-y-1">
          <p>
            <span className="text-sm text-text-secondary">Стоимость </span>
            <span className="font-mono text-lg" aria-label="Стоимость счёта">
              {snapshot.totalValue}
            </span>{' '}
            {snapshot.valueCurrency}
          </p>
          {snapshot.availableValue && (
            <p>
              <span className="text-sm text-text-secondary">Доступно </span>
              <span className="font-mono" aria-label="Доступно на счёте">
                {snapshot.availableValue}
              </span>{' '}
              {snapshot.valueCurrency}
            </p>
          )}
          <p
            className={`text-sm ${isStale(snapshot.capturedAt, now) ? 'text-warning-text' : 'text-muted'}`}
          >
            снимок {ageLabel(snapshot.capturedAt, now)} (<DateTime value={snapshot.capturedAt} />)
          </p>
        </div>
      ) : (
        <p className="text-sm text-muted">
          Снимков счёта пока нет: сервер читает счёт каждые пять минут, пока ключ использует бот.
        </p>
      )}
      <p className="text-sm">
        <span className="text-text-secondary">Капитал бота </span>
        <span className="font-mono" aria-label="Капитал бота">
          {withAsset(account.allocationCap, quoteAsset)}
        </span>
        <span className="text-text-secondary">
          {' '}
          ·{' '}
          {others > 0
            ? `ещё запущенных ботов на ключе: ${others}`
            : 'других запущенных ботов на ключе нет'}
        </span>
      </p>
      {account.failureReason && (
        <p className="text-sm text-danger-text">
          Последнее чтение счёта не удалось: {accountReadFailureLabel(account.failureReason)}
          {account.lastSuccessAt && (
            <>
              ; последний успешный снимок <DateTime value={account.lastSuccessAt} />
            </>
          )}
          .
        </p>
      )}
      {protection.open && (
        <p className="text-sm text-danger-text">
          Защита аккаунта остановила его ботов:{' '}
          {(protection.reason && protectionReasons[protection.reason]) ??
            protection.reason ??
            'причина неизвестна'}
          {protection.automaticResumeAt && (
            <>
              {' '}
              Возобновится после <DateTime value={protection.automaticResumeAt} />.
            </>
          )}
        </p>
      )}
      <Details account={account} now={now} />
    </>
  );
}
