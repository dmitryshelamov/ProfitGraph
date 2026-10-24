import { Outlet } from '@tanstack/react-router';

export function AppShell() {
  return (
    <div className="flex min-h-screen flex-col">
      <header className="border-b border-line bg-sidebar px-6 py-3">
        <span className="text-lg font-semibold">ProfitGraph</span>
      </header>
      <main className="flex-1 px-6 py-8">
        <Outlet />
      </main>
    </div>
  );
}
