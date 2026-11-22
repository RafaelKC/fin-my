import { expect, test } from '@playwright/test';

test('abre o app em pt-BR com o título Fin-My', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveTitle('Fin-My');
  await expect(page.locator('html')).toHaveAttribute('lang', 'pt-BR');
  await expect(page.getByRole('heading', { level: 1, name: 'Fin-My' })).toBeVisible();
});

test('não gera rolagem horizontal no viewport atual', async ({ page }) => {
  await page.goto('/');

  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  );
  expect(overflow).toBe(false);
});
