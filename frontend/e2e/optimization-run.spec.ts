import { randomUUID } from 'node:crypto';

import { expect, test } from '@playwright/test';

// The stage 4 optimization path: search a template's parameter in the browser, follow the trials, read every trial and
// open a finalist run. The Worker runs the optimization lane and Bybit is the local stub (e2e/bybit-stub.ts).
test('a template optimization runs from its setup to its trials and finalist runs', async ({
  page,
}) => {
  test.setTimeout(150_000);

  await page.goto('/strategies');
  await page.getByRole('button', { name: 'Новая стратегия' }).first().click();
  await page
    .getByRole('region', { name: 'Шаблоны' })
    .getByRole('button', { name: /^SMA Crossover\b/ })
    .click();
  await page.getByLabel('Название стратегии').fill(`E2E optimization ${randomUUID()}`);
  await page.getByRole('button', { name: 'Создать стратегию', exact: true }).click();
  await expect(page).toHaveURL(/\/strategies\/([0-9a-f-]+)\/edit$/);
  const strategyId = /\/strategies\/([0-9a-f-]+)\/edit$/.exec(page.url())![1];

  await page.goto(`/strategies/${strategyId}`);
  await page.getByRole('link', { name: 'Оптимизировать' }).click();
  const setup = page.getByRole('form', { name: 'Настройка оптимизации' });
  await expect(setup.getByLabel('Площадка')).toHaveValue('bybit');
  await setup.getByLabel('Пара').fill('ETHUSDT');
  await setup.getByLabel('Таймфрейм').selectOption('1h');
  await setup.getByLabel('С (UTC), пусто — с начала истории').fill('2026-01-02');
  await setup.getByLabel('По (UTC), пусто — до последней закрытой свечи').fill('2026-01-08');
  const first = setup.getByRole('group').first();
  await first.getByRole('checkbox').check();
  await first.getByLabel('До').fill('15');
  await first.getByLabel('От').fill('10');
  await setup.getByLabel('Сделок не меньше').fill('0');
  await setup.getByRole('button', { name: 'Запустить оптимизацию' }).click();
  await expect(page).toHaveURL(/\/optimizations\/[0-9a-f-]+$/);

  await expect(page.getByRole('status').filter({ hasText: 'Завершена' })).toBeVisible({
    timeout: 120_000,
  });
  const trials = page.getByRole('table', { name: 'Испытания' });
  await expect(trials.getByRole('row')).toHaveCount(7);
  await expect(page.getByText('Финалисты запущены')).toBeVisible();
  await page.getByRole('link', { name: 'Финалист 1' }).click();
  await expect(page).toHaveURL(/\/backtests\/[0-9a-f-]+$/);
  await expect(page.getByRole('status').filter({ hasText: 'Завершена' })).toBeVisible({
    timeout: 90_000,
  });
  await expect(page.getByLabel('Итоги прогона')).toBeVisible();
});
