import { Link } from '@tanstack/react-router';

export function NotFoundPage() {
  return (
    <section className="space-y-3">
      <h1 className="text-2xl font-semibold">Страница не найдена</h1>
      <Link to="/" className="text-link hover:underline">
        На главную
      </Link>
    </section>
  );
}
