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
  });

  test('mostra o nome do Espaço e a página Mês; /entrar com sessão volta para "/"', async ({
    page,
  }) => {
    await page.goto('/entrar');

    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByTestId('nome-do-espaco')).toHaveText('Espaço da Ana');
    await expect(page.getByRole('heading', { level: 1, name: 'Mês' })).toBeVisible();
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

  test('serve o manifest do PWA com o nome Fin-My', async ({ request }) => {
    const manifest = await (await request.get('/manifest.webmanifest')).json();
    expect(manifest.name).toBe('Fin-My');
    expect(manifest.display).toBe('standalone');
  });
});
