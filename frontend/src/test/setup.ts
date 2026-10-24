import '@testing-library/jest-dom/vitest';

// jsdom does not implement scrolling; the router restores scroll position on navigation.
window.scrollTo = () => undefined;

// jsdom does not implement ResizeObserver; React Flow measures its viewport with it.
globalThis.ResizeObserver ??= class implements ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
};
