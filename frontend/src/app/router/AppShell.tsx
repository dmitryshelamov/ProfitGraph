import { useQueryClient } from '@tanstack/react-query';
import { Link, Outlet, useNavigate } from '@tanstack/react-router';

import { signOut, useCurrentUser } from '@/modules/accounts';
import { Button } from '@/shared/ui/Button';

export function AppShell() {
  const user = useCurrentUser();
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  async function leave() {
    await signOut(queryClient);
    await navigate({ to: '/sign-in', search: {} });
  }

  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex flex-wrap items-center gap-x-6 gap-y-2 border-b border-line bg-sidebar px-6 py-3">
        <Link to="/strategies" className="text-lg font-semibold">
          ProfitGraph
        </Link>
        <nav aria-label="Разделы" className="flex gap-4 text-sm">
          <Link
            to="/strategies"
            className="text-text-secondary hover:text-text"
            activeProps={{ className: 'text-text', 'aria-current': 'page' }}
          >
            Стратегии
          </Link>
        </nav>
        {user && (
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span
              className="text-text-secondary"
              title={user.role === 'Owner' ? 'Владелец' : 'Пользователь'}
            >
              {user.login}
            </span>
            <Button size="sm" variant="ghost" onClick={() => void leave()}>
              Выйти
            </Button>
          </div>
        )}
      </header>
      <main className="flex-1 px-6 py-8">
        <Outlet />
      </main>
    </div>
  );
}
