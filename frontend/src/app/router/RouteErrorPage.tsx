import { useRouter } from '@tanstack/react-router';

import { Button } from '@/shared/ui/Button';

export function RouteErrorPage() {
  const router = useRouter();

  return (
    <section className="mx-auto max-w-md space-y-4 px-4 py-16 text-center">
      <h1 className="text-xl font-semibold">Не удалось связаться с сервером</h1>
      <p className="text-muted">Проверьте, что API запущен, и попробуйте ещё раз.</p>
      <Button onClick={() => void router.invalidate()}>Повторить</Button>
    </section>
  );
}
