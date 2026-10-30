import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { Bot } from '@/modules/bots';
import {
  AlertsList,
  BotAccountCard,
  botAlertsQuery,
  BotAssetCard,
  BotChart,
  BotControls,
  BotFeedList,
  BotFillsTable,
  botKeys,
  BotOpenOrders,
  botOpenOrdersQuery,
  type BotPageTab,
  botPageTabs,
  BotPositionCard,
  BotPositionClose,
  BotPositionCycles,
  botQuery,
  BotStatusBadge,
  botTitle,
  BotUnresolvedOrders,
  BotWaitingOrders,
  chartTimeframesFor,
  diagnosticLabel,
  ForeignOrdersToggle,
  fractionLabel,
  ManualOrderForm,
  ParametersForm,
  renameBot,
  RiskLimitsForm,
  shownTimeframe,
  TradingHaltBanner,
  useBotDisplay,
} from '@/modules/bots';
import { venuesQuery } from '@/modules/market-data';
import { isPercentageParameter, strategyQuery } from '@/modules/strategies';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';
import { RenameForm } from '@/shared/ui/RenameForm';

const route = getRouteApi('/_authenticated/_shell/bots/$botId');

/**
 * One bot, as a trading terminal: warnings that need attention above everything, then on the left its chart on a chosen
 * timeframe of its venue with its fills and working orders, and under it the orders, fills, event feed, alerts and settings
 * as tabs; on the right what drives the bot now: its state and controls, position at a live price, exchange account,
 * manual order and asset. The timeframe and the open tab are kept in the address.
 */
export function BotPage() {
  const { botId } = route.useParams();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const bot = useQuery(botQuery(botId));
  const strategyId = bot.data?.strategyId;
  const strategy = useQuery({
    ...strategyQuery(strategyId ?? ''),
    enabled: strategyId !== undefined,
  });

  const queryClient = useQueryClient();
  const [renaming, setRenaming] = useState(false);
  const rename = useMutation({
    mutationFn: (name: string) => renameBot(botId, name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: botKeys.all }),
  });
  const venues = useQuery(venuesQuery());
  const openOrders = useQuery(botOpenOrdersQuery(botId));
  const alerts = useQuery(botAlertsQuery(botId));
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
  const venue = venues.data?.find((item) => item.id === current.venue);
  const timeframes = chartTimeframesFor(current.timeframe, venue?.timeframes);
  const timeframe = shownTimeframe(search.timeframe, current.timeframe, timeframes);
  const tab: BotPageTab = search.tab ?? 'orders';
  const counts: Partial<Record<BotPageTab, number>> = {
    orders: openOrders.data?.length,
    alerts: alerts.data?.filter((alert) => alert.acknowledgedAt === null).length,
  };

  const openTab = (next: BotPageTab) =>
    void navigate({
      search: (previous) => ({ ...previous, tab: next === 'orders' ? undefined : next }),
      replace: true,
    });

  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <PageHeader
        title={botTitle(current)}
        titleAction={
          <Button size="sm" variant="ghost" onClick={() => setRenaming(true)}>
            Переименовать
          </Button>
        }
        editor={
          renaming ? (
            <RenameForm
              initial={current.name ?? ''}
              label="Название бота"
              maxLength={100}
              allowEmpty
              onSave={(name) => rename.mutateAsync(name)}
              onDone={() => setRenaming(false)}
            />
          ) : undefined
        }
        back={
          <Link to="/bots" className="text-sm text-text-secondary hover:text-text">
            ← Боты
          </Link>
        }
        meta={
          <>
            {venue?.name ?? current.venue} · стратегия{' '}
            <Link
              to="/strategies/$strategyId"
              params={{ strategyId: current.strategyId }}
              className="text-accent hover:underline"
            >
              {strategy.data?.name ?? 'открыть стратегию'}
            </Link>{' '}
            · комиссии: лимитные {fractionLabel(current.limitFeeRate)}, рыночные{' '}
            {fractionLabel(current.marketFeeRate)}
          </>
        }
      />
      <TradingHaltBanner />
      <BotUnresolvedOrders botId={current.id} />
      {current.diagnostics.length > 0 && (
        <ul
          aria-label="Диагностика"
          className="list-disc space-y-1 rounded-lg border border-warning-border bg-warning-bg py-3 pr-4 pl-9 text-sm text-warning-text"
        >
          {current.diagnostics.map((diagnostic) => (
            <li key={diagnostic}>{diagnosticLabel(diagnostic)}</li>
          ))}
        </ul>
      )}
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_24rem]">
        <div className="min-w-0 space-y-6">
          <Card
            title="График"
            actions={
              <ChartTimeframes bot={current} timeframes={timeframes} timeframe={timeframe} />
            }
          >
            {/* A timeframe from the address waits for the venue's list, so one it does not offer is never asked for. */}
            {venues.isPending && search.timeframe !== undefined ? (
              <p className="text-sm text-muted">Загрузка свечей…</p>
            ) : (
              <BotChart bot={current} display={display} timeframe={timeframe} />
            )}
          </Card>
          <Card>
            <div
              role="tablist"
              aria-label="Работа бота"
              className="mb-4 flex flex-wrap gap-x-5 gap-y-2 border-b border-line"
            >
              {botPageTabs.map((item) => (
                <button
                  key={item.id}
                  id={`bot-tab-${item.id}`}
                  type="button"
                  role="tab"
                  aria-selected={tab === item.id}
                  aria-controls={`bot-panel-${item.id}`}
                  onClick={() => openTab(item.id)}
                  className={`-mb-px border-b-2 px-1 pb-2 text-sm ${
                    tab === item.id
                      ? 'border-text text-text'
                      : 'border-transparent text-text-secondary hover:text-text'
                  }`}
                >
                  {item.label}
                  {counts[item.id] ? (
                    <span className="ml-1.5 text-muted">{counts[item.id]}</span>
                  ) : null}
                </button>
              ))}
            </div>
            <div
              role="tabpanel"
              id={`bot-panel-${tab}`}
              aria-labelledby={`bot-tab-${tab}`}
              className="overflow-x-auto"
            >
              {tab === 'orders' && (
                <div className="space-y-6">
                  <BotOpenOrders bot={current} display={display} />
                  <BotWaitingOrders botId={current.id} />
                </div>
              )}
              {tab === 'fills' && (
                <div className="space-y-6">
                  <BotPositionCycles botId={current.id} display={display} />
                  <BotFillsTable botId={current.id} display={display} />
                </div>
              )}
              {tab === 'feed' && <BotFeedList botId={current.id} display={display} />}
              {tab === 'alerts' && <AlertsList botId={current.id} />}
              {tab === 'settings' && (
                <div className="space-y-8">
                  <section aria-labelledby="bot-risk-limits" className="space-y-3">
                    <h3 id="bot-risk-limits" className="font-semibold">
                      Лимиты риска
                    </h3>
                    <RiskLimitsForm
                      key={`${current.maxOrderQuote}/${current.dailyLossLimitQuote}`}
                      bot={current}
                    />
                  </section>
                  <section aria-labelledby="bot-parameters" className="space-y-3">
                    <h3 id="bot-parameters" className="font-semibold">
                      Параметры
                    </h3>
                    <ParametersForm
                      key={`${current.updatedAt}/${percentParameters.size}`}
                      bot={current}
                      percentParameters={percentParameters}
                    />
                    <ForeignOrdersToggle bot={current} />
                  </section>
                </div>
              )}
            </div>
          </Card>
        </div>
        <aside aria-label="Управление ботом" className="space-y-6">
          <Card title="Состояние" actions={<BotStatusBadge bot={current} />}>
            <div className="space-y-4">
              {current.nextRestartAt && (
                <p className="text-sm text-muted">
                  Перезапуск после <DateTime value={current.nextRestartAt} />
                </p>
              )}
              <BotControls bot={current} />
              <p className="text-sm text-muted">
                Обновлён <DateTime value={current.updatedAt} />
              </p>
            </div>
          </Card>
          <Card title="Позиция">
            <div className="space-y-4">
              <BotPositionCard botId={current.id} display={display} />
              <BotPositionClose bot={current} display={display} />
            </div>
          </Card>
          <BotAccountCard botId={current.id} quoteAsset={display.quoteAsset} />
          <Card title="Ручная заявка">
            <ManualOrderForm bot={current} />
          </Card>
          <BotAssetCard bot={current} />
        </aside>
      </div>
    </div>
  );
}

/**
 * The chart's timeframes as links that keep the open tab; the bot's own one is marked and stays out of the address. The link
 * of the address's timeframe is the current page.
 */
function ChartTimeframes({
  bot,
  timeframes,
  timeframe,
}: {
  bot: Bot;
  timeframes: readonly string[];
  timeframe: string;
}) {
  return (
    <div role="group" aria-label="Таймфрейм" className="flex flex-wrap items-center gap-1">
      {timeframes.map((item) => (
        <Link
          key={item}
          from="/bots/$botId"
          to="/bots/$botId"
          params={{ botId: bot.id }}
          search={(previous) => ({
            ...previous,
            timeframe: item === bot.timeframe ? undefined : item,
          })}
          replace
          activeOptions={{ exact: true }}
          className={
            item === timeframe
              ? 'rounded-md border border-link px-2.5 py-1 text-sm text-link'
              : 'rounded-md border border-line-strong px-2.5 py-1 text-sm text-text-secondary hover:text-text'
          }
        >
          {item === bot.timeframe ? `${item} · бот` : item}
        </Link>
      ))}
      <span className="ml-2 text-sm text-muted">UTC</span>
    </div>
  );
}
