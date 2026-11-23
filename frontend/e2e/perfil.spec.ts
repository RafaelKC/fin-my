import { expect, test, type Page } from '@playwright/test';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Ana', email: 'ana@example.com' },
  tenant: { id: 't1', name: 'Espaço da Ana', role: 'owner' },
};

async function comSessao(page: Page) {
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
}

test.describe('perfil e sair (US5) — API simulada', () => {
  test.beforeEach(async ({ page }) => {
    await comSessao(page);
  });

  test('altera o nome do Espaço e o topo reflete', async ({ page }) => {
    let corpo: unknown;
    await page.route('**/api/v1/tenant', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({ json: { ...sessao.tenant, name: 'Casa da Ana' } });
    });

    await page.goto('/perfil');
    await expect(page.getByTestId('nome-do-espaco')).toHaveText('Espaço da Ana');
    await page.getByLabel('Nome do Espaço').fill('Casa da Ana');
    await page.getByRole('button', { name: 'Salvar Espaço' }).click();

    await expect(page.getByTestId('espaco-salvo')).toBeVisible();
    await expect(page.getByTestId('nome-do-espaco')).toHaveText('Casa da Ana');
    expect(corpo).toEqual({ name: 'Casa da Ana' });
  });

  test('altera o nome do usuário', async ({ page }) => {
    let corpo: unknown;
    await page.route('**/api/v1/me', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({
        json: { user: { ...sessao.user, name: 'Ana Souza' }, tenant: sessao.tenant },
      });
    });

    await page.goto('/perfil');
    await page.getByLabel('Nome', { exact: true }).fill('Ana Souza');
    await page.getByRole('button', { name: 'Salvar nome' }).click();

    await expect(page.getByTestId('nome-salvo')).toBeVisible();
    expect(corpo).toEqual({ name: 'Ana Souza' });
  });

  test('troca a senha (204)', async ({ page }) => {
    let corpo: unknown;
    await page.route('**/api/v1/me/password', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({ status: 204 });
    });

    await page.goto('/perfil');
    await page.getByLabel('Senha atual').fill('senha-antiga-1');
    await page.getByLabel('Nova senha').fill('senha-nova-123');
    await page.getByRole('button', { name: 'Trocar senha' }).click();

    await expect(page.getByTestId('senha-trocada')).toBeVisible();
    expect(corpo).toEqual({ currentPassword: 'senha-antiga-1', newPassword: 'senha-nova-123' });
  });

  test('senha atual errada (422) mostra o erro no campo', async ({ page }) => {
    await page.route('**/api/v1/me/password', (route) =>
      route.fulfill({
        status: 422,
        contentType: 'application/problem+json',
        json: {
          status: 422,
          code: 'current_password_incorrect',
          detail: 'A senha atual está incorreta.',
        },
      }),
    );

    await page.goto('/perfil');
    await page.getByLabel('Senha atual').fill('errada-12345');
    await page.getByLabel('Nova senha').fill('senha-nova-123');
    await page.getByRole('button', { name: 'Trocar senha' }).click();

    await expect(page.getByText('A senha atual está incorreta.')).toBeVisible();
  });

  test('Sair encerra a sessão e leva a /entrar; /perfil volta a pedir login', async ({ page }) => {
    let saiu = false;
    await page.route('**/api/v1/auth/logout', (route) => {
      saiu = true;
      return route.fulfill({ status: 204 });
    });

    await page.goto('/perfil');
    await page.getByRole('button', { name: 'Sair' }).click();
    await expect(page).toHaveURL(/\/entrar$/);
    expect(saiu).toBe(true);

    // Sem sessão no servidor, o refresh passa a recusar.
    await page.unroute('**/api/v1/auth/refresh');
    await page.route('**/api/v1/auth/refresh', (route) =>
      route.fulfill({
        status: 401,
        contentType: 'application/problem+json',
        json: { status: 401, code: 'unauthorized', detail: 'Sessão inválida.' },
      }),
    );
    await page.goto('/perfil');
    await expect(page).toHaveURL(/\/entrar\?returnUrl=%2Fperfil$/);
  });
});

// Edge Case da troca de senha com DOIS contextos de navegador (dois aparelhos): ao trocar a
// senha no aparelho A, o aparelho B cai na próxima ação (401 session_revoked → /entrar), e A
// continua logado. Depende da API real: T027 (POST /me/password revoga as outras sessões) e
// T015 (SessionValidationMiddleware). Pulado até as duas entrarem na main; depois basta trocar
// `test.skip` por `test` (com a stack do compose no ar).
test.skip('troca de senha em um aparelho derruba o outro na próxima ação', async ({ browser }) => {
  const email = `ana+${Date.now()}@example.com`;
  const senhaAntiga = 'senha-antiga-1';
  const senhaNova = 'senha-nova-123';

  const aparelhoA = await browser.newContext();
  const aparelhoB = await browser.newContext();
  const a = await aparelhoA.newPage();
  const b = await aparelhoB.newPage();

  await a.goto('/criar-conta');
  await a.getByLabel('Nome').fill('Ana Souza');
  await a.getByLabel('E-mail').fill(email);
  await a.getByLabel('Senha', { exact: true }).fill(senhaAntiga);
  await a.getByRole('button', { name: 'Criar conta' }).click();
  await expect(a).toHaveURL(/\/$/);

  await b.goto('/entrar');
  await b.getByLabel('E-mail').fill(email);
  await b.getByLabel('Senha', { exact: true }).fill(senhaAntiga);
  await b.getByRole('button', { name: 'Entrar' }).click();
  await expect(b).toHaveURL(/\/$/);

  await a.goto('/perfil');
  await a.getByLabel('Senha atual').fill(senhaAntiga);
  await a.getByLabel('Nova senha').fill(senhaNova);
  await a.getByRole('button', { name: 'Trocar senha' }).click();
  await expect(a.getByTestId('senha-trocada')).toBeVisible();

  // Aparelho B: a próxima ação autenticada cai no login.
  await b.getByRole('link', { name: 'Perfil' }).click();
  await b.getByRole('button', { name: 'Salvar nome' }).click();
  await expect(b).toHaveURL(/\/entrar/);

  // Aparelho A segue logado.
  await a.reload();
  await expect(a).toHaveURL(/\/perfil$/);

  await aparelhoA.close();
  await aparelhoB.close();
});
