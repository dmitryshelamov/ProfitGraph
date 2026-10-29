import { getRouteApi } from '@tanstack/react-router';

export const backtestsRoute = getRouteApi('/_authenticated/_shell/backtests');

/** The page's tab and filters as the address holds them. */
export type ListSearch = ReturnType<typeof backtestsRoute.useSearch>;

/** Changes some filters; an undefined value removes the filter from the address. */
export type UpdateSearch = (changes: Partial<ListSearch>) => void;
