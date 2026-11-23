import { expect, test } from '@playwright/test';

// Sem sessão (o refresh da abertura falha), o app cai em /entrar.
test.beforeEach(async ({ page }) => {
  await page.route('**/api/v1/auth/refresh', (route) =>
    route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ status: 401, code: 'session_expired', detail: 'Sessão expirada.' }),
    }),
  );
});

test('abre o app em pt-BR com o título Fin-My', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveTitle('Fin-My');
  await expect(page.locator('html')).toHaveAttribute('lang', 'pt-BR');
  await expect(page.getByRole('heading', { level: 1, name: 'Fin-My' })).toBeVisible();
});

test('não gera rolagem horizontal no viewport atual', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveURL(/\/entrar/);

  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  );
  expect(overflow).toBe(false);
});
