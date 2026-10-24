import '@testing-library/jest-dom/vitest';

// jsdom does not implement scrolling; the router restores scroll position on navigation.
window.scrollTo = () => undefined;

// jsdom does not implement ResizeObserver; React Flow measures its viewport with it.
globalThis.ResizeObserver ??= class implements ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
};

// jsdom has no canvas, so the chart library is replaced by series that keep the data they were given.
vi.mock('lightweight-charts', () => {
  const series = () => ({
    setData: vi.fn(),
    applyOptions: vi.fn(),
    priceScale: () => ({ applyOptions: vi.fn() }),
  });

  return {
    CandlestickSeries: 'Candlestick',
    LineSeries: 'Line',
    ColorType: { Solid: 'solid' },
    createChart: () => ({
      addSeries: series,
      removeSeries: vi.fn(),
      remove: vi.fn(),
      timeScale: () => ({ fitContent: vi.fn() }),
    }),
  };
});

// No test opens a real hub connection; src/test/fakeHub.ts lets a test emit progress messages.
vi.mock('@microsoft/signalr', async () => (await import('./fakeHub')).signalrModule);

afterEach(async () => {
  (await import('./fakeHub')).hub.reset();
});
