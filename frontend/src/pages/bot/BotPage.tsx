import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';

import {
  AlertsList,
  BotControls,
  BotFeedList,
  BotFillsTable,
  BotOpenOrders,
  BotPositionCard,
  botQuery,
  BotStatusBadge,
  BotUnresolvedOrders,
  BotWaitingOrders,
  diagnosticLabel,
  ForeignOrdersToggle,
  fractionLabel,
  ManualOrderForm,
  ParametersForm,
  RiskLimitsForm,
  TradingHaltBanner,
  useBotDisplay,
} from '@/modules/bots';
import { venuesQuery } from '@/modules/market-data';
import { isPercentageParameter, strategyQuery } from '@/modules/strategies';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';

/**
 * One bot: its state and controls, position at a live price, limits, parameters, foreign orders, alerts, manual order,
 * event feed and fills.
 */
export function BotPage() {
  const { botId } = useParams({ from: '/_authenticated/_shell/bots/$botId' });
  const bot = useQuery(botQuery(botId));
  const strategyId = bot.data?.strategyId;
  const strategy = useQuery({
    ...strategyQuery(strategyId ?? ''),
    enabled: strategyId !== undefined,
  });

  const venues = useQuery(venuesQuery());
  const display = useBotDisplay({
    venue: bot.data?.venue ?? '',
    symbol: bot.data?.symbol ?? '',
    credentialId: bot.data?.credentialId ?? '',
  });
  const percentParameters = new Set(
    (strategy.data?.definition.parameters ?? [])
      .filter((parameter) => isPercentageParameter(parameter.displayUnit))
      .map((parameter) => parameter.name),
  );

  if (bot.isError) {
    return (
      <div className="mx-auto max-w-5xl">
        <ApiErrorMessage error={bot.error} fallback="Не удалось загрузить бота." />
      </div>
    );
  }

  if (!bot.data) {
    return <p className="text-muted">Загрузка…</p>;
  }

  const current = bot.data;

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <PageHeader
        title={`${current.symbol} ${current.timeframe}`}
        back={
          <Link to="/bots" className="text-sm text-text-secondary hover:text-text">
            ← Боты
          </Link>
        }
        meta={`${venues.data?.find((venue) => venue.id === current.venue)?.name ?? current.venue} · комиссии: лимитные ${fractionLabel(current.limitFeeRate)}, рыночные ${fractionLabel(current.marketFeeRate)}`}
      />
      <p className="text-sm text-text-secondary">
        Стратегия:{' '}
        <Link
          to="/strategies/$strategyId"
          params={{ strategyId: current.strategyId }}
          className="text-accent hover:underline"
        >
          {strategy.data?.name ?? 'открыть стратегию'}
        </Link>
      </p>
      <TradingHaltBanner />
      <Card title="Состояние" actions={<BotStatusBadge bot={current} />}>
        <div className="space-y-4">
          {current.diagnostics.length > 0 && (
            <ul className="list-disc pl-5 text-sm text-text-secondary" aria-label="Диагностика">
              {current.diagnostics.map((diagnostic) => (
                <li key={diagnostic}>{diagnosticLabel(diagnostic)}</li>
              ))}
            </ul>
          )}
          {current.nextRestartAt && (
            <p className="text-sm text-muted">
              Перезапуск после <DateTime value={current.nextRestartAt} /> (попытка{' '}
              {current.restartAttempts})
            </p>
          )}
          <BotControls bot={current} />
          <p className="text-sm text-muted">
            Обновлён <DateTime value={current.updatedAt} />
          </p>
        </div>
      </Card>
      <Card title="Позиция">
        <BotPositionCard botId={current.id} display={display} />
      </Card>
      <div className="grid gap-6 md:grid-cols-2">
        <Card title="Лимиты риска">
          <RiskLimitsForm
            key={`${current.maxOrderQuote}/${current.dailyLossLimitQuote}`}
            bot={current}
          />
        </Card>
        <Card title="Ручная заявка">
          <ManualOrderForm bot={current} />
        </Card>
      </div>
      <Card title="Параметры">
        <div className="space-y-4">
          <ParametersForm
            key={`${current.updatedAt}/${percentParameters.size}`}
            bot={current}
            percentParameters={percentParameters}
          />
          <ForeignOrdersToggle bot={current} />
        </div>
      </Card>
      <BotUnresolvedOrders botId={current.id} />
      <Card title="Активные ордера">
        <BotOpenOrders botId={current.id} display={display} />
      </Card>
      <BotWaitingOrders botId={current.id} />
      <Card title="Алерты">
        <AlertsList botId={current.id} />
      </Card>
      <Card title="Лента">
        <BotFeedList botId={current.id} />
      </Card>
      <Card title="Сделки">
        <BotFillsTable botId={current.id} display={display} />
      </Card>
    </div>
  );
}
