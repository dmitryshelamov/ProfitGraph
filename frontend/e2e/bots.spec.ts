import { randomUUID } from 'node:crypto';

import { expect, test } from '@playwright/test';

import type { components } from '../src/shared/api/generated/schema';

type Schemas = components['schemas'];

/**
 * A key and a bot are created in the UI, the bot is started and stopped. Trading stays disabled in the e2e Worker, so a
 * started bot degrades with "trading is disabled in this process" and nothing reaches an exchange; the key is invented.
 */
test('a bot is created with a key, started and stopped', async ({ page }) => {
  const templates = (await (
    await page.request.get('/api/strategy-templates')
  ).json()) as Schemas['StrategyTemplateResponse'][];
  const template = templates.find((item) => item.kind === 'LadderSma') ?? templates[0]!;
  const strategyName = `E2E bot ${randomUUID()}`;
  const created = await page.request.post('/api/strategies', {
    data: { name: strategyName, definition: template.definition, layout: template.layout },
  });
  expect(created.status()).toBe(201);
  const keyLabel = `E2E key ${randomUUID().slice(0, 8)}`;

  await page.goto('/credentials');
  const keyForm = page.getByRole('form', { name: 'Новый ключ биржи' });
  await keyForm.getByLabel('Название ключа').fill(keyLabel);
  await keyForm.getByLabel('API key').fill('e2e-invented-key-0000');
  await keyForm.getByLabel('API secret').fill('e2e-invented-secret');
  await keyForm.getByRole('button', { name: 'Сохранить ключ' }).click();
  await expect(page.getByRole('cell', { name: keyLabel })).toBeVisible();

  await page.getByRole('link', { name: 'Боты' }).click();
  await page.getByRole('button', { name: 'Новый бот' }).click();
  const botForm = page.getByRole('form', { name: 'Новый бот' });
  await botForm.getByLabel('Стратегия').selectOption({ label: strategyName });
  await botForm.getByLabel('Ключ биржи').selectOption({ label: `${keyLabel} (…0000)` });
  await botForm.getByLabel('Символ', { exact: true }).fill('BTCUSDT');
  await botForm.getByLabel('Таймфрейм').selectOption('1h');
  await botForm.getByRole('button', { name: 'Создать бота' }).click();

  await expect(page.getByRole('heading', { name: 'BTCUSDT 1h' })).toBeVisible();
  await expect(page.getByText('Не запущен')).toBeVisible();
  await page.getByRole('button', { name: 'Запустить', exact: true }).click();
  await expect(
    page.getByText('Остановлен по безопасности: торговля в этом процессе выключена'),
  ).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Остановить', exact: true }).click();
  await expect(page.getByText(/^Остановлен: торговля в этом процессе выключена$/)).toBeVisible();
});
