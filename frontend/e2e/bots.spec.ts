import { randomUUID } from 'node:crypto';

import { expect, test } from '@playwright/test';

import type { components } from '../src/shared/api/generated/schema';

type Schemas = components['schemas'];

/**
 * A key and a Ladder bot are created in the UI, the bot is started, buys at once and is stopped. Trading stays disabled:
 * the e2e Worker trades against its in-memory simulation (market orders fill at 100 with a 0.1% fee) and the stub
 * pushes a ticker of 104, so the position is valued at the live price. Nothing reaches an exchange; the key is invented.
 */
test('a bot is created with a key, trades, is stopped and reported', async ({ page }) => {
  // The page polls the bot's state, fills and feed every 5, 15 and 10 seconds; waiting for each took 29.6 of the
  // default 30 seconds before the chart was added.
  test.setTimeout(60_000);
  const templates = (await (
    await page.request.get('/api/strategy-templates')
  ).json()) as Schemas['StrategyTemplateResponse'][];
  const template = templates.find((item) => item.kind === 'Ladder')!;
  const definition = {
    ...template.definition,
    parameters: template.definition.parameters.map((parameter) =>
      parameter.name === 'buyAmount' ? { ...parameter, defaultValue: '10' } : parameter,
    ),
  };
  const strategyName = `E2E bot ${randomUUID()}`;
  const created = await page.request.post('/api/strategies', {
    data: { name: strategyName, definition, layout: template.layout },
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
  // The chart reads the bot's own hourly history from the stub's public klines into the cache.
  const chart = page.getByRole('figure', { name: 'Свечи бота' });
  await expect(chart).not.toHaveAttribute('data-candles', '0', { timeout: 20_000 });
  await page.getByRole('button', { name: 'Запустить', exact: true }).click();
  await expect(page.getByText('Работает', { exact: true })).toBeVisible({ timeout: 20_000 });

  // OnStart buys for 10 USDT at 100: 0.1 BTC less a 0.0001 BTC fee, valued at the ticker: 0.0999 × 104 − 10.
  const fills = page.getByRole('table', { name: 'Сделки бота' });
  await expect(fills.getByRole('cell', { name: '100', exact: true })).toBeVisible({
    timeout: 30_000,
  });
  await expect(fills.getByRole('cell', { name: '0.0001 BTC', exact: true })).toBeVisible();
  await expect(page.getByLabel('Цена для отображения')).toHaveText('104 USDT', { timeout: 20_000 });
  await expect(page.getByText(/^тикер биржи/)).toBeVisible();
  await expect(page.getByLabel('Нереализованная прибыль')).toHaveText('0.3896 USDT');
  const feed = page.getByRole('list', { name: 'Лента бота' });
  await expect(feed.getByText('Запущен', { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(feed.getByText(/^Исполнение: покупка 0\.1 на 10/)).toBeVisible({ timeout: 20_000 });

  await page.getByRole('button', { name: 'Остановить', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Запустить', exact: true })).toBeVisible({
    timeout: 20_000,
  });
  await expect(feed.getByText('Остановлен', { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText('Базовый актив')).toBeVisible();
});
