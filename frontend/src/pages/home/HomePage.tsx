import { ApiHealthBadge } from '@/modules/system';

export function HomePage() {
  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">ProfitGraph</h1>
      <p className="text-text-secondary">Конструктор торговых стратегий</p>
      <ApiHealthBadge />
    </section>
  );
}
