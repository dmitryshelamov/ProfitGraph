import { useQueryClient } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import { type FormEvent, useState } from 'react';

import { signIn } from '@/modules/accounts';
import { ApiHealthBadge } from '@/modules/system';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { safeReturnPath } from './safeReturnPath';

const route = getRouteApi('/sign-in');

export function SignInPage() {
  const queryClient = useQueryClient();
  const navigate = route.useNavigate();
  const { returnTo } = route.useSearch();
  const [login, setLogin] = useState('');
  const [password, setPassword] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);

    try {
      const result = await signIn(queryClient, login, password);

      if ('failure' in result) {
        setFailure(result.failure);

        return;
      }

      await navigate({ href: safeReturnPath(returnTo), replace: true });
    } catch {
      setFailure('Не удалось связаться с сервером.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <form
        className="w-full max-w-sm space-y-4 rounded-lg border border-line bg-panel p-6"
        onSubmit={(event) => void submit(event)}
      >
        <div className="space-y-1">
          <span className="text-sm font-semibold text-accent">ProfitGraph</span>
          <h1 className="text-xl font-semibold">Вход</h1>
          <p className="text-sm text-muted">Введите логин и пароль, выданные владельцем.</p>
        </div>
        <Field label="Логин">
          <Input
            autoComplete="username"
            value={login}
            onChange={(event) => setLogin(event.target.value)}
            required
          />
        </Field>
        <Field label="Пароль">
          <Input
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </Field>
        {failure && (
          <p role="alert" className="text-sm text-danger-text">
            {failure}
          </p>
        )}
        <Button type="submit" variant="primary" className="w-full" disabled={submitting}>
          {submitting ? 'Входим…' : 'Войти'}
        </Button>
        <div className="flex justify-center">
          <ApiHealthBadge />
        </div>
      </form>
    </div>
  );
}
