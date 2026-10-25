import { useMutation, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { continueEnrollment, credentialKeys, startEnrollment } from '../api/credentialsApi';
import { steamEnrollmentProblems } from '../model/steam';
import type { CredentialEnrollment } from '../model/types';

/**
 * Links a new Steam Guard authenticator to a Steam account in steps (ADR 0022): sign in with the account name and
 * password, type the code Steam emails, write down the recovery code shown once, then activate with the code from the
 * next email. The password and codes leave this form in their own request only and are cleared once sent; the recovery
 * code is never shown again.
 */
export function SteamEnrollmentForm({ onCreated }: { onCreated?: () => void }) {
  const queryClient = useQueryClient();
  const [label, setLabel] = useState('');
  const [accountName, setAccountName] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [saved, setSaved] = useState(false);
  const [enrollment, setEnrollment] = useState<CredentialEnrollment | null>(null);
  const step = useMutation({
    mutationFn: (fields: Readonly<Record<string, string>>) =>
      enrollment
        ? continueEnrollment('steam', enrollment.enrollmentId, enrollment.step, fields)
        : startEnrollment('steam', label.trim(), fields),
    onSuccess: async (next) => {
      setPassword('');
      setCode('');
      setSaved(false);
      setEnrollment(next);

      if (next.step === 'completed') {
        await queryClient.invalidateQueries({ queryKey: credentialKeys.all });
        onCreated?.();
      }
    },
  });
  const problem = enrollment?.problem ? steamEnrollmentProblems[enrollment.problem] : undefined;

  function submit(event: FormEvent) {
    event.preventDefault();

    if (!enrollment) {
      step.mutate({ accountName: accountName.trim(), password });
    } else if (enrollment.step === 'email-code' || enrollment.step === 'activate') {
      step.mutate({ code: code.trim() });
    } else {
      step.mutate({});
    }
  }

  function restart() {
    setEnrollment(null);
    step.reset();
  }

  return (
    <form onSubmit={submit} className="grid gap-4 md:grid-cols-2" aria-label="Привязка Steam Guard">
      {!enrollment && (
        <>
          <Field label="Название ключа">
            <Input
              value={label}
              onChange={(event) => setLabel(event.target.value)}
              required
              maxLength={100}
            />
          </Field>
          <Field label="Имя аккаунта Steam">
            <Input
              value={accountName}
              onChange={(event) => setAccountName(event.target.value)}
              required
              autoComplete="off"
              spellCheck={false}
            />
          </Field>
          <Field label="Пароль Steam">
            <Input
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
              autoComplete="current-password"
              spellCheck={false}
            />
          </Field>
          <p className="text-sm text-muted md:col-span-2">
            Пароль используется один раз для входа и не сохраняется. Для аккаунта с существующим
            аутентификатором используйте импорт командой steam-import.
          </p>
        </>
      )}
      {(enrollment?.step === 'email-code' || enrollment?.step === 'activate') && (
        <Field
          label={
            enrollment.step === 'email-code'
              ? 'Код из письма Steam'
              : 'Код активации из письма Steam'
          }
        >
          <Input
            value={code}
            onChange={(event) => setCode(event.target.value)}
            required
            autoComplete="one-time-code"
            spellCheck={false}
          />
        </Field>
      )}
      {enrollment?.step === 'sign-in-pending' && (
        <p className="text-sm md:col-span-2">Steam ещё подтверждает вход.</p>
      )}
      {enrollment?.step === 'recovery-saved' && (
        <div className="space-y-3 md:col-span-2">
          <p className="text-sm">
            Код восстановления Steam Guard показан один раз. Запишите его: без него аккаунт не
            вернуть, если сервер потеряет аутентификатор.
          </p>
          <p className="font-mono text-2xl" aria-label="Код восстановления">
            {enrollment.oneTimeSecret}
          </p>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={saved}
              onChange={(event) => setSaved(event.target.checked)}
            />
            Я записал код восстановления
          </label>
        </div>
      )}
      {enrollment?.step === 'completed' && (
        <p className="text-sm md:col-span-2">
          Аутентификатор привязан. Steam выдерживает новый аутентификатор около 15 дней, затем
          аккаунт проверяется.
        </p>
      )}
      {problem && (
        <p role="alert" className="text-danger-text md:col-span-2">
          {problem}
        </p>
      )}
      <div className="flex items-center gap-3 md:col-span-2">
        {enrollment?.step === 'completed' || enrollment?.step === 'stopped' ? (
          <Button type="button" onClick={restart}>
            Привязать другой аккаунт
          </Button>
        ) : (
          <Button
            type="submit"
            variant="primary"
            disabled={step.isPending || (enrollment?.step === 'recovery-saved' && !saved)}
          >
            {!enrollment
              ? 'Войти в Steam'
              : enrollment.step === 'sign-in-pending'
                ? 'Проверить вход'
                : enrollment.step === 'recovery-saved'
                  ? 'Продолжить'
                  : enrollment.step === 'activate'
                    ? 'Активировать'
                    : 'Отправить код'}
          </Button>
        )}
      </div>
      {step.isError && (
        <div className="md:col-span-2">
          <ApiErrorMessage error={step.error} fallback="Не удалось выполнить шаг привязки." />
        </div>
      )}
    </form>
  );
}
