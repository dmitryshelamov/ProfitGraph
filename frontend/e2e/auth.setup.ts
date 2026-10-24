import { mkdirSync } from 'node:fs';

import { expect, test } from '@playwright/test';

test('sign in once as the run’s bootstrap Owner', async ({ page }) => {
  await page.goto('/strategies');
  await expect(page).toHaveURL(/\/sign-in\?returnTo=/);
  await page.getByLabel('Логин').fill('e2e-owner');
  await page.getByLabel('Пароль').fill(process.env.PROFITGRAPH_E2E_PASSWORD!);
  await page.getByRole('button', { name: 'Войти' }).click();
  await expect(page).toHaveURL('/strategies');
  await expect(page.getByText('e2e-owner')).toBeVisible();
  mkdirSync('test-results/.auth', { recursive: true });
  await page.context().storageState({ path: 'test-results/.auth/owner.json' });
});
