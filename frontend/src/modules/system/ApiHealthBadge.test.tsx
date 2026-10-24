import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';

import { ApiHealthBadge } from './ApiHealthBadge';

function renderBadge() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={queryClient}>
      <ApiHealthBadge />
    </QueryClientProvider>,
  );
}

describe('ApiHealthBadge', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the checking state while the request is pending', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {})),
    );

    renderBadge();

    expect(screen.getByRole('status')).toHaveTextContent('Проверка…');
  });

  it('shows that the API is available when the health check is healthy', async () => {
    const fetchMock = vi.fn(async () => new Response('Healthy', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    renderBadge();

    expect(await screen.findByText('API доступен')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith('/api/health', expect.anything());
  });

  it('shows that the API is unavailable when the health check fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('Unhealthy', { status: 503 })),
    );

    renderBadge();

    expect(await screen.findByText('API недоступен')).toBeInTheDocument();
  });

  it('shows that the API is unavailable when the request cannot be sent', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))),
    );

    renderBadge();

    expect(await screen.findByText('API недоступен')).toBeInTheDocument();
  });
});
