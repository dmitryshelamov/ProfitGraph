import type { QueryClient } from '@tanstack/react-query';
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  lazyRouteComponent,
  Outlet,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';

import { currentUserQuery } from '@/modules/accounts';
import { botPageSearch } from '@/modules/bots';
import { scanChartTimeframe } from '@/modules/scanner';

import { AppShell } from './AppShell';
import { NotFoundPage } from './NotFoundPage';
import { RouteErrorPage } from './RouteErrorPage';

export interface RouterContext {
  readonly queryClient: QueryClient;
}

/**
 * The runs page's tab and filters in the address, so they survive a reload and can be shared. Values the page does not
 * know are dropped; absent values filter nothing.
 */
export interface BacktestsSearch {
  tab?: 'optimizations';
  q?: string;
  strategy?: string;
  venue?: string;
  timeframe?: string;
  status?: 'failed' | 'cancelled';
  quote?: string;
  origin?: 'manual' | 'optimization';
  from?: string;
  to?: string;
  minReturn?: string;
  maxDrawdown?: string;
  sort?: 'return' | 'pnl' | 'drawdown' | 'created';
  order?: 'asc' | 'desc';
  state?: 'active' | 'completed' | 'failed' | 'cancelled';
  method?: string;
}

const searchChoices: { [Key in keyof BacktestsSearch]-?: readonly string[] | null } = {
  tab: ['optimizations'],
  q: null,
  strategy: null,
  venue: null,
  timeframe: null,
  status: ['failed', 'cancelled'],
  quote: null,
  origin: ['manual', 'optimization'],
  from: null,
  to: null,
  minReturn: null,
  maxDrawdown: null,
  sort: ['return', 'pnl', 'drawdown', 'created'],
  order: ['asc', 'desc'],
  state: ['active', 'completed', 'failed', 'cancelled'],
  method: null,
};

function backtestsSearch(search: Record<string, unknown>): BacktestsSearch {
  return Object.fromEntries(
    Object.entries(searchChoices).flatMap(([key, choices]) => {
      // A typed number arrives parsed from the address; the filters keep it as the text the user typed.
      const raw = search[key];
      const value = typeof raw === 'number' && choices === null ? String(raw) : raw;

      return typeof value === 'string' &&
        value !== '' &&
        (choices === null || choices.includes(value))
        ? [[key, value]]
        : [];
    }),
  );
}

export interface SignInSearch {
  readonly returnTo?: string;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({
  component: Outlet,
  notFoundComponent: NotFoundPage,
  errorComponent: RouteErrorPage,
});

const signInRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/sign-in',
  validateSearch: (search: Record<string, unknown>): SignInSearch =>
    typeof search.returnTo === 'string' ? { returnTo: search.returnTo } : {},
  beforeLoad: async ({ context }) => {
    if (await context.queryClient.ensureQueryData(currentUserQuery())) {
      throw redirect({ to: '/' });
    }
  },
  component: lazyRouteComponent(() => import('@/pages/sign-in/SignInPage'), 'SignInPage'),
});

/** Every screen except sign-in needs a session; without one the user is sent to sign in and brought back. */
const authenticatedRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: '_authenticated',
  beforeLoad: async ({ context, location }) => {
    if (!(await context.queryClient.ensureQueryData(currentUserQuery()))) {
      throw redirect({ to: '/sign-in', search: { returnTo: location.href } });
    }
  },
  component: Outlet,
});

/** Regular screens share the navigation shell; the editor uses the whole window. */
const shellRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  id: '_shell',
  component: AppShell,
});

const homeRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/strategies' });
  },
});

const strategiesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies',
  component: lazyRouteComponent(
    () => import('@/pages/strategies/StrategiesPage'),
    'StrategiesPage',
  ),
});

const strategyRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies/$strategyId',
  component: lazyRouteComponent(() => import('@/pages/strategy/StrategyPage'), 'StrategyPage'),
});

const backtestSetupRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies/$strategyId/backtest',
  component: lazyRouteComponent(
    () => import('@/pages/backtest-setup/BacktestSetupPage'),
    'BacktestSetupPage',
  ),
});

const backtestsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/backtests',
  validateSearch: backtestsSearch,
  component: lazyRouteComponent(() => import('@/pages/backtests/BacktestsPage'), 'BacktestsPage'),
});

const backtestRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/backtests/$runId',
  component: lazyRouteComponent(
    () => import('@/pages/backtest/BacktestRunPage'),
    'BacktestRunPage',
  ),
});

const optimizationSetupRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies/$strategyId/optimize',
  component: lazyRouteComponent(
    () => import('@/pages/optimization-setup/OptimizationSetupPage'),
    'OptimizationSetupPage',
  ),
});

const optimizationRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/optimizations/$optimizationId',
  component: lazyRouteComponent(
    () => import('@/pages/optimization/OptimizationPage'),
    'OptimizationPage',
  ),
});

const compareRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/compare',
  validateSearch: (search: Record<string, unknown>): { runs?: string; trials?: string } => ({
    ...(typeof search.runs === 'string' ? { runs: search.runs } : {}),
    ...(typeof search.trials === 'string' ? { trials: search.trials } : {}),
  }),
  component: lazyRouteComponent(() => import('@/pages/compare/ComparePage'), 'ComparePage'),
});

const historicalDataRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/historical-data',
  component: lazyRouteComponent(
    () => import('@/pages/historical-data/HistoricalDataPage'),
    'HistoricalDataPage',
  ),
});

const botsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/bots',
  component: lazyRouteComponent(() => import('@/pages/bots/BotsPage'), 'BotsPage'),
});

const botRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/bots/$botId',
  validateSearch: botPageSearch,
  component: lazyRouteComponent(() => import('@/pages/bot/BotPage'), 'BotPage'),
});

const credentialsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/credentials',
  component: lazyRouteComponent(
    () => import('@/pages/credentials/CredentialsPage'),
    'CredentialsPage',
  ),
});

const scannerRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/scanner',
  component: lazyRouteComponent(() => import('@/pages/scanner/ScannerPage'), 'ScannerPage'),
});

const scanPairRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/scanner/$venue/$symbol',
  validateSearch: (search: Record<string, unknown>): { timeframe?: string } => {
    const timeframe = scanChartTimeframe(search.timeframe);

    return timeframe ? { timeframe } : {};
  },
  component: lazyRouteComponent(() => import('@/pages/scanner/ScanPairPage'), 'ScanPairPage'),
});

const scanBacktestRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/scanner/backtests/$backtestId',
  component: lazyRouteComponent(
    () => import('@/pages/scanner/ScanBacktestPage'),
    'ScanBacktestPage',
  ),
});

const profileRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/profile',
  component: lazyRouteComponent(() => import('@/pages/profile/ProfilePage'), 'ProfilePage'),
});

const strategyEditorRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/strategies/$strategyId/edit',
  component: lazyRouteComponent(
    () => import('@/pages/strategy-editor/StrategyEditorPage'),
    'StrategyEditorPage',
  ),
});

const routeTree = rootRoute.addChildren([
  signInRoute,
  authenticatedRoute.addChildren([
    shellRoute.addChildren([
      homeRoute,
      strategiesRoute,
      strategyRoute,
      backtestSetupRoute,
      backtestsRoute,
      backtestRoute,
      optimizationSetupRoute,
      optimizationRoute,
      compareRoute,
      historicalDataRoute,
      botsRoute,
      botRoute,
      credentialsRoute,
      scannerRoute,
      scanPairRoute,
      scanBacktestRoute,
      profileRoute,
    ]),
    strategyEditorRoute,
  ]),
]);

export function createAppRouter(queryClient: QueryClient, history?: RouterHistory) {
  return createRouter({ routeTree, context: { queryClient }, history });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
