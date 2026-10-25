import { useQuery } from '@tanstack/react-query';

import {
  AccountOverview,
  CredentialForm,
  credentialsQuery,
  CredentialsTable,
  SteamAccountCard,
} from '@/modules/credentials';
import { Card } from '@/shared/ui/Card';
import { PageHeader } from '@/shared/ui/PageHeader';

/** The user's exchange keys: stored encrypted, shown by their last four characters only. */
export function CredentialsPage() {
  const credentials = useQuery(credentialsQuery());

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <PageHeader
        title="Ключи бирж"
        meta="Ключ нужен боту для торговли; секрет после сохранения не показывается."
      />
      <Card title="Новый ключ">
        <CredentialForm />
      </Card>
      <Card title="Сохранённые ключи">
        {credentials.isError ? (
          <p role="alert" className="text-danger-text">
            Не удалось загрузить ключи.
          </p>
        ) : !credentials.data || credentials.data.length === 0 ? (
          <p className="text-muted">{credentials.isPending ? 'Загрузка…' : 'Ключей пока нет.'}</p>
        ) : (
          <CredentialsTable credentials={credentials.data} />
        )}
      </Card>
      {credentials.data?.map((credential) => (
        <Card key={credential.id} title={`Счёт: ${credential.label}`}>
          <div className="space-y-4">
            {credential.venue === 'steam' && <SteamAccountCard credential={credential} />}
            <AccountOverview credentialId={credential.id} />
          </div>
        </Card>
      ))}
    </div>
  );
}
