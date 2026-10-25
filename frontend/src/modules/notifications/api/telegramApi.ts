import { queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

export const telegramKeys = {
  link: ['profile', 'telegram'] as const,
};

export function telegramLinkQuery() {
  return queryOptions({
    queryKey: telegramKeys.link,
    queryFn: async () => unwrap(await api.GET('/api/profile/telegram')),
  });
}

/** A one-time code to send to the bot as `/start CODE` within ten minutes. */
export async function createTelegramLinkCode() {
  return unwrap(await api.POST('/api/profile/telegram/link-code'));
}

export async function unlinkTelegram(): Promise<void> {
  const result = await api.DELETE('/api/profile/telegram');

  if (!result.response.ok) {
    unwrap(result);
  }
}
