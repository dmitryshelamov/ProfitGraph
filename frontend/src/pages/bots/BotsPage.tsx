import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { botsQuery, BotsTable, CreateBotForm, TradingHaltBanner } from '@/modules/bots';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { PageHeader } from '@/shared/ui/PageHeader';

/** The user's live bots with their state; a new bot is created stopped. */
export function BotsPage() {
  const bots = useQuery(botsQuery());
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <PageHeader
        title="Боты"
        meta="Боты торгуют стратегией на Bybit Spot, только в лонг, по закрытым свечам."
        actions={
          !creating && (
            <Button variant="primary" onClick={() => setCreating(true)}>
              Новый бот
            </Button>
          )
        }
      />
      <TradingHaltBanner />
      {creating && (
        <Card
          title="Новый бот"
          actions={
            <Button variant="ghost" size="sm" onClick={() => setCreating(false)}>
              Отмена
            </Button>
          }
        >
          <CreateBotForm
            onCreated={(bot) => void navigate({ to: '/bots/$botId', params: { botId: bot.id } })}
          />
        </Card>
      )}
      <Card>
        {bots.isError ? (
          <p role="alert" className="text-danger-text">
            Не удалось загрузить ботов.
          </p>
        ) : !bots.data || bots.data.length === 0 ? (
          <p className="text-muted">{bots.isPending ? 'Загрузка…' : 'Ботов пока нет.'}</p>
        ) : (
          <BotsTable bots={bots.data} />
        )}
      </Card>
    </div>
  );
}
