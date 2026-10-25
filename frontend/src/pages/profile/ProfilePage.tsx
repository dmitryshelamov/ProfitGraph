import { useCurrentUser } from '@/modules/accounts';
import { TelegramLinkCard } from '@/modules/notifications';
import { Card } from '@/shared/ui/Card';
import { PageHeader } from '@/shared/ui/PageHeader';

/** The signed-in user's profile: who they are and where their bot alerts go. */
export function ProfilePage() {
  const user = useCurrentUser();

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <PageHeader
        title="Профиль"
        meta={
          user
            ? `${user.login} · ${user.role === 'Owner' ? 'владелец' : 'пользователь'}`
            : undefined
        }
      />
      <Card title="Алерты в Telegram">
        <TelegramLinkCard />
      </Card>
    </div>
  );
}
