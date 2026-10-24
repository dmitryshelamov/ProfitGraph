import '@testing-library/jest-dom/vitest';

// jsdom does not implement scrolling; the router restores scroll position on navigation.
window.scrollTo = () => undefined;
