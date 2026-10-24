import { randomUUID } from 'node:crypto';

import { expect, test } from '@playwright/test';

// The stage 3 acceptance path (roadmap): pick a pair, download its history, run a template and read its chart. The
// Worker runs the jobs and Bybit is the local stub (e2e/bybit-stub.ts).
test('a template backtest runs from pair selection to the result chart', async ({ page }) => {
  test.setTimeout(120_000);

  await page.goto('/historical-data');
  const download = page.getByRole('form', { name: 'Загрузка истории' });
  await expect(download.getByLabel('Площадка')).toHaveValue('bybit');
  const pair = download.getByLabel('Пара');
  await pair.fill('ETH');
  await expect(page.locator('datalist option[value="ETHUSDT"]')).toHaveCount(1);
  await pair.fill('ETHUSDT');
  await download.getByLabel('Таймфрейм').selectOption('1h');
  await download.getByLabel('С (UTC), пусто — с начала истории').fill('2026-01-01');
  await download.getByLabel('По (UTC), пусто — до последней закрытой свечи').fill('2026-01-08');
  await download.getByRole('button', { name: 'Загрузить историю' }).click();
  await expect(page.getByText('Завершена')).toBeVisible({ timeout: 60_000 });
  await expect(page.getByText(/Свечей в периоде: 168/)).toBeVisible();

  await page.goto('/strategies');
  await page.getByRole('button', { name: 'Новая стратегия' }).first().click();
  await page
    .getByRole('region', { name: 'Шаблоны' })
    .getByRole('button', { name: /^SMA Crossover\b/ })
    .click();
  await page.getByLabel('Название стратегии').fill(`E2E chart ${randomUUID()}`);
  await page.getByRole('button', { name: 'Создать стратегию', exact: true }).click();
  await expect(page).toHaveURL(/\/strategies\/[0-9a-f-]+\/edit$/);
  // The editor loads lazily; the shell's "Прогоны" link must not be mistaken for its run button.
  await expect(page.getByRole('region', { name: 'Блок-схема стратегии' })).toBeVisible();
  await page.getByRole('link', { name: 'Прогон', exact: true }).click();

  const setup = page.getByRole('form', { name: 'Настройка прогона' });
  await expect(setup.getByLabel('Площадка')).toHaveValue('bybit');
  await setup.getByLabel('Пара').fill('ETHUSDT');
  await setup.getByLabel('Таймфрейм').selectOption('1h');
  await setup.getByLabel('С (UTC), пусто — с начала истории').fill('2026-01-02');
  await setup.getByLabel('По (UTC), пусто — до последней закрытой свечи').fill('2026-01-08');
  await setup.getByRole('button', { name: 'Запустить прогон' }).click();
  await expect(page).toHaveURL(/\/backtests\/[0-9a-f-]+$/);

  await expect(page.getByRole('status').filter({ hasText: 'Завершена' })).toBeVisible({
    timeout: 90_000,
  });
  await expect(page.getByLabel('Итоги прогона')).toBeVisible();
  const chart = page.getByRole('figure', { name: 'График прогона' });
  await expect(chart).toHaveAttribute('data-points', '144');
  await expect(chart.locator('canvas').first()).toBeVisible();
  await expect(page.getByRole('rowheader', { name: 'Отложенная выборка' })).toBeVisible();
  // The stub's history starts long before the period, so the warm-up the strategy asks for is supplied in full.
  await expect(page.getByText(/^(\d+) из \1 свечей$/)).toBeVisible();

  await page
    .getByRole('navigation', { name: 'Разделы' })
    .getByRole('link', { name: 'Прогоны' })
    .click();
  await expect(page.getByRole('table').getByText('ETHUSDT').first()).toBeVisible();
});
