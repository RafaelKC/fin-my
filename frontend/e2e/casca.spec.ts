import { expect, test } from '@playwright/test';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Ana', email: 'ana@example.com' },
  tenant: { id: 't1', name: 'Espaço da Ana', role: 'owner' },
};

test.describe('casca do app logado', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
    await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [] }));
    await page.route('**/api/v1/months/**', (route) => {
      const mes = new URL(route.request().url()).pathname.split('/').pop();
      const zero = {
        openingBalanceCents: 0,
        walletsInitialBalanceCents: 0,
        incomeCents: 0,
        expenseCents: 0,
        resultCents: 0,
        transfersInCents: 0,
        transfersOutCents: 0,
        finalBalanceCents: 0,
      };
      return route.fulfill({
        json: {
          month: mes,
          today: '2026-03-10',
          walletIds: null,
          planned: zero,
          actual: zero,
          entries: [],
          transfers: [],
          pending: { count: 0, toReceiveCents: 0, toPayCents: 0 },
        },
      });
    });
  });

  test('mostra o nome do Espaço e a visão do mês; /entrar com sessão volta para o mês de hoje', async ({
    page,
  }) => {
    await page.goto('/entrar');

    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    await expect(page.getByTestId('nome-do-espaco')).toHaveText('Espaço da Ana');
    await expect(page.getByRole('heading', { level: 1, name: /de \d{4}$/ })).toBeVisible();
  });

  test('navegação: barra inferior no celular, menu lateral a partir de 1024 px', async ({
    page,
  }, info) => {
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Principal' });
    const caixa = await nav.boundingBox();
    const viewport = page.viewportSize()!;

    expect(caixa).not.toBeNull();
    if (info.project.name === 'mobile') {
      expect(caixa!.width).toBe(viewport.width);
      expect(caixa!.y + caixa!.height).toBe(viewport.height);
    } else {
      expect(caixa!.x).toBe(0);
      expect(caixa!.width).toBeLessThan(viewport.width / 2);
    }

    const alvo = await nav.getByRole('link', { name: 'Mês' }).boundingBox();
    expect(alvo!.width).toBeGreaterThanOrEqual(44);
    expect(alvo!.height).toBeGreaterThanOrEqual(44);
  });

  test('Cartões na barra; Carteiras e Perfil em "Mais" no celular e direto no menu lateral', async ({
    page,
  }, info) => {
    await page.route('**/api/v1/credit-cards**', (route) => route.fulfill({ json: [] }));
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Principal' });
    await expect(nav.getByRole('link', { name: 'Cartões' })).toBeVisible();

    if (info.project.name === 'mobile') {
      await expect(nav.getByRole('link', { name: 'Carteiras' })).toBeHidden();
      await nav.getByTestId('mais').click();
      await expect(nav.getByRole('link', { name: 'Carteiras' })).toBeVisible();
      await expect(nav.getByRole('link', { name: 'Perfil' })).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(nav.getByRole('link', { name: 'Carteiras' })).toBeHidden();
    } else {
      await expect(nav.getByTestId('mais')).toBeHidden();
      await expect(nav.getByRole('link', { name: 'Carteiras' })).toBeVisible();
      await expect(nav.getByRole('link', { name: 'Perfil' })).toBeVisible();
    }

    await nav.getByRole('link', { name: 'Cartões' }).click();
    await expect(page).toHaveURL(/\/cartoes$/);
  });

  test('serve o manifest do PWA com o nome Fin-My', async ({ request }) => {
    const manifest = await (await request.get('/manifest.webmanifest')).json();
    expect(manifest.name).toBe('Fin-My');
    expect(manifest.display).toBe('standalone');
  });
});
