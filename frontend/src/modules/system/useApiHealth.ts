import { useQuery } from '@tanstack/react-query';

export type ApiHealth = 'checking' | 'healthy' | 'unavailable';

/** `/api/health` is a plain-text health check outside the OpenAPI document, so it is called with `fetch`. */
async function fetchApiHealth(): Promise<string> {
  const response = await fetch('/api/health', { headers: { Accept: 'text/plain' } });

  if (!response.ok) {
    throw new Error(`API health check failed with status ${response.status}.`);
  }

  return response.text();
}

export function useApiHealth(): ApiHealth {
  const query = useQuery({ queryKey: ['system', 'health'], queryFn: fetchApiHealth });

  if (query.isPending) {
    return 'checking';
  }

  return query.isSuccess && query.data.trim() === 'Healthy' ? 'healthy' : 'unavailable';
}
