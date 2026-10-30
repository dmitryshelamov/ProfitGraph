import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { pageBefore } from '@/modules/market-chart';
import { api, ApiError, unwrap } from '@/shared/api/client';

import { isActiveClose } from '../model/positionClose';
import type {
  Bot,
  BotCandlesPage,
  BotPositionClose,
  ManualOrder,
  OrderCancel,
  OrderSide,
  TradingHalt,
} from '../model/types';

export const botKeys = {
  all: ['bots'] as const,
  detail: (id: string) => ['bots', id] as const,
  alerts: (id: string) => ['bots', id, 'alerts'] as const,
  manualOrder: (id: string, orderId: string) => ['bots', id, 'manual-orders', orderId] as const,
  tradingHalt: ['trading-halt'] as const,
  liveImpact: (strategyId: string) => ['strategies', strategyId, 'live-impact'] as const,
  fills: (id: string) => ['bots', id, 'fills'] as const,
  feed: (id: string) => ['bots', id, 'feed'] as const,
  position: (id: string) => ['bots', id, 'position'] as const,
  account: (id: string) => ['bots', id, 'account'] as const,
  waiting: (id: string) => ['bots', id, 'waiting-orders'] as const,
  openOrders: (id: string) => ['bots', id, 'open-orders'] as const,
  orderCancel: (id: string, cancelId: string) => ['bots', id, 'order-cancels', cancelId] as const,
  unresolved: (id: string) => ['bots', id, 'unresolved-orders'] as const,
  candles: (id: string, timeframe: string) => ['bots', id, 'candles', timeframe] as const,
  positionClose: (id: string) => ['bots', id, 'position-close'] as const,
  newCandles: (id: string, timeframe: string, from: string) =>
    ['bots', id, 'new-candles', timeframe, from] as const,
};

/** A missing bot and a bot of another user are both 404; retrying cannot change that. */
function retryUnlessMissing(count: number, error: Error): boolean {
  return !(error instanceof ApiError && error.status === 404) && count < 2;
}

export function botsQuery() {
  return queryOptions({
    queryKey: botKeys.all,
    queryFn: async () => unwrap(await api.GET('/api/bots')),
    refetchInterval: 10_000,
  });
}

/** The runtime changes a bot's state on its own, so the page follows it. */
export function botQuery(id: string) {
  return queryOptions({
    queryKey: botKeys.detail(id),
    queryFn: async () => unwrap(await api.GET('/api/bots/{id}', { params: { path: { id } } })),
    retry: retryUnlessMissing,
    refetchInterval: 5_000,
  });
}

/**
 * The exchange account the bot trades on, from what the server stored (ADR 0049): reading it never reaches the venue, and
 * the Worker refreshes the value every five minutes, so a minute's polling is enough.
 */
export function botAccountQuery(id: string) {
  return queryOptions({
    queryKey: botKeys.account(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/bots/{id}/account', { params: { path: { id } } })),
    retry: retryUnlessMissing,
    refetchInterval: 60_000,
  });
}

export interface CreateBotInput {
  readonly strategyId: string;
  readonly credentialId: string;
  /** The bot's venue, one that takes the key (ADR 0061); the key's own when absent. */
  readonly venue?: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly parameters: Readonly<Record<string, string>>;
  readonly allocationCap: string;
  readonly maxOrderQuote: string;
  readonly dailyLossLimitQuote: string;
  readonly allowForeignOrders: boolean;
  /** The venue module's own settings, such as a Steam bot's market-buy slippage; the venue's defaults when absent. */
  readonly venueSettings?: Readonly<Record<string, unknown>>;
}

/** Creates a stopped bot; the server reads the key's fee rates for the symbol. */
export async function createBot(input: CreateBotInput): Promise<Bot> {
  return unwrap(
    await api.POST('/api/bots', { body: { ...input, parameters: { ...input.parameters } } }),
  );
}

export async function startBot(id: string): Promise<Bot> {
  return unwrap(await api.POST('/api/bots/{id}/start', { params: { path: { id } } }));
}

export async function stopBot(id: string): Promise<Bot> {
  return unwrap(await api.POST('/api/bots/{id}/stop', { params: { path: { id } } }));
}

/** The kill switch: the bot stops at once, its unsent orders are rejected and its open orders cancelled. */
export async function killBot(id: string): Promise<Bot> {
  return unwrap(await api.POST('/api/bots/{id}/kill', { params: { path: { id } } }));
}

export async function changeRiskLimits(
  id: string,
  maxOrderQuote: string,
  dailyLossLimitQuote: string,
): Promise<Bot> {
  return unwrap(
    await api.PUT('/api/bots/{id}/risk-limits', {
      params: { path: { id } },
      body: { maxOrderQuote, dailyLossLimitQuote },
    }),
  );
}

export async function changeParameters(
  id: string,
  parameters: Readonly<Record<string, string>>,
  allocationCap: string,
): Promise<Bot> {
  return unwrap(
    await api.PUT('/api/bots/{id}/parameters', {
      params: { path: { id } },
      body: { parameters: { ...parameters }, allocationCap },
    }),
  );
}

export async function changeForeignOrders(id: string, allowForeignOrders: boolean): Promise<Bot> {
  return unwrap(
    await api.PUT('/api/bots/{id}/foreign-orders', {
      params: { path: { id } },
      body: { allowForeignOrders },
    }),
  );
}

export function botAlertsQuery(id: string) {
  return queryOptions({
    queryKey: botKeys.alerts(id),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/bots/{id}/alerts', { params: { path: { id }, query: { limit: 50 } } }),
      ),
    refetchInterval: 15_000,
  });
}

export async function acknowledgeAlert(id: string) {
  return unwrap(await api.POST('/api/alerts/{id}/acknowledge', { params: { path: { id } } }));
}

/** An owner order: a buy's amount is quote, a sell's base; a limit order also carries its price. The runner takes it within a minute. */
export async function submitManualOrder(
  botId: string,
  order: { side: OrderSide; amount: string; limitPrice?: string },
): Promise<ManualOrder> {
  return unwrap(
    await api.POST('/api/bots/{id}/manual-orders', {
      params: { path: { id: botId } },
      body:
        order.limitPrice === undefined
          ? { side: order.side, amount: order.amount }
          : {
              side: order.side,
              type: 'Limit',
              amount: order.amount,
              limitPrice: order.limitPrice,
            },
    }),
  );
}

/** Follows a manual order until the runner resolves it. */
export function manualOrderQuery(botId: string, orderId: string) {
  return queryOptions({
    queryKey: botKeys.manualOrder(botId, orderId),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/bots/{id}/manual-orders/{orderId}', {
          params: { path: { id: botId, orderId } },
        }),
      ),
    refetchInterval: (query) => (query.state.data?.status === 'Pending' ? 1_000 : false),
  });
}

/** Asks the bot's runner to close its position (ADR 0064): cancel the bot's orders, sell at the market, stop when asked. */
export async function requestPositionClose(
  botId: string,
  stopAfter: boolean,
): Promise<BotPositionClose> {
  return unwrap(
    await api.POST('/api/bots/{id}/position-close', {
      params: { path: { id: botId } },
      body: { stopAfter },
    }),
  );
}

/** The bot's newest position close, or null when it never had one; followed every second while it works. */
export function botPositionCloseQuery(botId: string) {
  return queryOptions({
    queryKey: botKeys.positionClose(botId),
    queryFn: async (): Promise<BotPositionClose | null> => {
      try {
        return unwrap(
          await api.GET('/api/bots/{id}/position-close', { params: { path: { id: botId } } }),
        );
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) {
          return null;
        }

        throw error;
      }
    },
    refetchInterval: (query) =>
      query.state.data && isActiveClose(query.state.data) ? 1_000 : false,
  });
}

export function tradingHaltQuery() {
  return queryOptions({
    queryKey: botKeys.tradingHalt,
    queryFn: async () => unwrap(await api.GET('/api/trading-halt')),
    refetchInterval: 30_000,
  });
}

/** The Owner's global kill switch: every bot that should run is killed and nothing starts until resumed. */
export async function haltTrading(): Promise<TradingHalt> {
  return unwrap(await api.POST('/api/trading-halt/halt'));
}

export async function resumeTrading(): Promise<TradingHalt> {
  return unwrap(await api.POST('/api/trading-halt/resume'));
}

/** The running bots a save of the strategy switches to the new version. */
export function liveImpactQuery(strategyId: string) {
  return queryOptions({
    queryKey: botKeys.liveImpact(strategyId),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/strategies/{id}/live-impact', { params: { path: { id: strategyId } } }),
      ),
  });
}

/** The bot's open orders as its engine holds them: at the venue, being submitted or being cancelled. */
export function botOpenOrdersQuery(id: string) {
  return queryOptions({
    queryKey: botKeys.openOrders(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/bots/{id}/open-orders', { params: { path: { id } } })),
    refetchInterval: 15_000,
  });
}

/**
 * The owner cancels one of the running bot's open orders (ADR 0048). The bot's runner turns it into the engine's ordinary
 * cancel within a minute; follow it with {@link orderCancelQuery}.
 */
export async function cancelBotOrder(botId: string, engineOrderId: number): Promise<OrderCancel> {
  return unwrap(
    await api.POST('/api/bots/{id}/open-orders/{engineOrderId}/cancel', {
      params: { path: { id: botId, engineOrderId } },
    }),
  );
}

/** Follows an owner's order cancel until the runner resolves it. */
export function orderCancelQuery(botId: string, cancelId: string) {
  return queryOptions({
    queryKey: botKeys.orderCancel(botId, cancelId),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/bots/{id}/order-cancels/{cancelId}', {
          params: { path: { id: botId, cancelId } },
        }),
      ),
    refetchInterval: (query) => (query.state.data?.status === 'Pending' ? 1_000 : false),
  });
}

const pageSize = 50;

/** The bot's fills, newest first, a page at a time. */
/** The bot's working orders its venue holds locally, with why. */
export function botWaitingOrdersQuery(id: string) {
  return queryOptions({
    queryKey: botKeys.waiting(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/bots/{id}/waiting-orders', { params: { path: { id } } })),
    refetchInterval: 15_000,
  });
}

/** The bot's submits whose delivery to the venue is unknown, and whether each may be confirmed absent now. */
export function botUnresolvedOrdersQuery(id: string) {
  return queryOptions({
    queryKey: botKeys.unresolved(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/bots/{id}/unresolved-orders', { params: { path: { id } } })),
    refetchInterval: 15_000,
  });
}

/**
 * The owner confirms, after checking the account on the venue, that an unresolved submit never reached it. Restarting
 * the bot then looks the order up again and rejects it only when the venue shows nothing.
 */
export async function confirmOrderAbsent(botId: string, intentId: string): Promise<void> {
  const result = await api.POST('/api/bots/{id}/order-intents/{intentId}/confirm-absent', {
    params: { path: { id: botId, intentId } },
  });

  if (!result.response.ok) {
    unwrap(result);
  }
}

export function botFillsQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: botKeys.fills(id),
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api.GET('/api/bots/{id}/fills', {
          params: {
            path: { id },
            query: { limit: pageSize, ...(pageParam === null ? {} : { before: pageParam }) },
          },
        }),
      ),
    initialPageParam: null as number | null,
    getNextPageParam: (page) => page.next ?? undefined,
    refetchInterval: 15_000,
  });
}

/** The bot's event feed, newest first, a page at a time; entries older than 90 days are gone. */
export function botFeedQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: botKeys.feed(id),
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api.GET('/api/bots/{id}/feed', {
          params: {
            path: { id },
            query: { limit: pageSize, ...(pageParam === null ? {} : { before: pageParam }) },
          },
        }),
      ),
    initialPageParam: null as number | null,
    getNextPageParam: (page) => page.next ?? undefined,
    refetchInterval: 10_000,
  });
}

/** The position at its display price, with recent cycles. */
export function botPositionQuery(id: string) {
  return queryOptions({
    queryKey: botKeys.position(id),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/bots/{id}/position', {
          params: { path: { id }, query: { cycles: 20 } },
        }),
      ),
    refetchInterval: 10_000,
  });
}

/** Names a bot in any state without touching trading; an empty name clears it. */
export async function renameBot(id: string, name: string): Promise<Bot> {
  return unwrap(await api.PUT('/api/bots/{id}/name', { params: { path: { id } }, body: { name } }));
}

async function botCandles(
  id: string,
  timeframe: string,
  from: string,
  to: string,
): Promise<BotCandlesPage> {
  return unwrap(
    await api.GET('/api/bots/{id}/candles', {
      params: { path: { id }, query: { from, to, timeframe } },
    }),
  );
}

/**
 * The bot's instrument's closed candles on a timeframe of its venue with the bot's fills, a page of `chartPageCandles`
 * candles at a time going back from now; the server cuts each page at the latest closed candle. Paging stops at a page without candles: the venue's history begins
 * there or cannot be read. Pages are kept as loaded; {@link botNewCandlesQuery} brings the candles closed since.
 */
export function botCandlesQuery(id: string, timeframe: string) {
  return infiniteQueryOptions({
    queryKey: botKeys.candles(id, timeframe),
    queryFn: async ({ pageParam }) => {
      const window = pageBefore(pageParam ?? new Date().toISOString(), timeframe);

      return botCandles(id, timeframe, window.from, window.to);
    },
    initialPageParam: null as string | null,
    getNextPageParam: (page) => (page.candles.length === 0 ? undefined : page.from),
    staleTime: Number.POSITIVE_INFINITY,
    refetchOnWindowFocus: false,
  });
}

/** Candles closed since `from`, the end of the newest loaded page, read again every minute. */
export function botNewCandlesQuery(id: string, timeframe: string, from: string) {
  return queryOptions({
    queryKey: botKeys.newCandles(id, timeframe, from),
    queryFn: async () => botCandles(id, timeframe, from, new Date().toISOString()),
    refetchInterval: 60_000,
  });
}
