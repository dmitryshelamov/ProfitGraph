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

import { AppShell } from './AppShell';
import { NotFoundPage } from './NotFoundPage';
import { RouteErrorPage } from './RouteErrorPage';

export interface RouterContext {
  readonly queryClient: QueryClient;
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
