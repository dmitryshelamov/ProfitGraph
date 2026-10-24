import { randomUUID } from 'node:crypto';

import { expect, test } from '@playwright/test';

import type { components } from '../src/shared/api/generated/schema';

const candles = [
  'openTime,open,high,low,close,volume',
  '2026-01-01T00:00:00Z,100,110,90,105,1',
  '2026-01-01T01:00:00Z,105,112,100,110,2',
  '2026-01-01T02:00:00Z,110,115,104,108,3',
].join('\n');

test('the Owner imports candles from CSV', async ({ page }) => {
  const symbol = `E2E${randomUUID().replaceAll('-', '').slice(0, 8).toUpperCase()}USDT`;
  await page.goto('/historical-data');
  const form = page.getByRole('form', { name: 'Импорт CSV' });
  await expect(form.getByLabel('Площадка')).toHaveValue('bybit');
  await form.getByLabel('Пара').fill(symbol);
  await form.getByLabel('Таймфрейм').selectOption('1h');
  await form
    .getByLabel('Файл CSV')
    .setInputFiles({ name: 'candles.csv', mimeType: 'text/csv', buffer: Buffer.from(candles) });
  await form.getByRole('button', { name: 'Импортировать' }).click();

  await expect(form.getByText(/Принято свечей: 3/)).toBeVisible();
  const stored = await page.request.get(
    `/api/historical-data/candles?venue=bybit&symbol=${symbol}&timeframe=1h&from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z`,
  );
  expect((await stored.json()) as unknown[]).toHaveLength(3);
});

test('a run of a template is set up from the strategy page and queued', async ({ page }) => {
  await page.goto('/strategies');
  await page.getByRole('button', { name: 'Новая стратегия' }).first().click();
  await page
    .getByRole('region', { name: 'Шаблоны' })
    .getByRole('button', { name: /^SMA Crossover\b/ })
    .click();
  await page.getByLabel('Название стратегии').fill(`E2E run ${randomUUID()}`);
  await page.getByRole('button', { name: 'Создать стратегию', exact: true }).click();
  await expect(page).toHaveURL(/\/strategies\/[0-9a-f-]+\/edit$/);
  const strategyPath = new URL(page.url()).pathname.replace(/\/edit$/, '');

  await page.getByRole('link', { name: 'Прогон' }).click();
  await expect(page).toHaveURL(`${strategyPath}/backtest`);
  const form = page.getByRole('form', { name: 'Настройка прогона' });
  await expect(form.getByLabel('Площадка')).toHaveValue('bybit');
  await form.getByLabel('С (UTC), пусто — с начала истории').fill('2026-01-01');
  await form.getByLabel('По (UTC), пусто — до последней закрытой свечи').fill('2026-01-03');
  await form.getByLabel('Начальный капитал, в валюте котировки').fill('500');
  await form.getByRole('button', { name: 'Запустить прогон' }).click();

  await expect(page.getByText('Прогон поставлен в очередь')).toBeVisible();
  // End-to-end tests run no Worker, so the run stays queued with its frozen settings.
  await expect(page.getByText('В очереди')).toBeVisible();
  const runs = (await (
    await page.request.get(`/api${strategyPath}/backtests`)
  ).json()) as components['schemas']['BacktestSummaryPageResponse'];
  expect(runs.items).toHaveLength(1);
  const run = (await (
    await page.request.get(`/api/backtests/${runs.items[0]!.id}`)
  ).json()) as components['schemas']['BacktestRunResponse'];
  expect(run.configuration).toMatchObject({
    venue: 'bybit',
    symbol: 'BTCUSDT',
    timeframe: '1h',
    startingCapital: '500',
    limitFeeRate: '0.001',
    outOfSampleFraction: '0.2',
  });
  expect(new Date(run.configuration.from!).toISOString()).toBe('2026-01-01T00:00:00.000Z');
  expect(new Date(run.configuration.to!).toISOString()).toBe('2026-01-03T00:00:00.000Z');
});
