import { expect, test, type Page } from '@playwright/test';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Ana Souza', email: 'ana@example.com' },
  tenant: { id: 't1', name: 'Finanças de Ana', role: 'owner' },
};

const semSessao = {
  status: 401,
  contentType: 'application/problem+json',
  json: { status: 401, code: 'unauthorized', detail: 'Sessão inválida.' },
};

async function preencher(page: Page, senha = 'senha-segura') {
  await page.getByLabel('E-mail').fill('ana@example.com');
  await page.getByLabel('Senha', { exact: true }).fill(senha);
}

test.describe('entrar (US2) — API simulada', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill(semSessao));
  });

  test('entra e chega ao app com o Espaço da pessoa', async ({ page }) => {
    let corpo: unknown;
    await page.route('**/api/v1/auth/login', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({ json: sessao });
    });

    await page.goto('/entrar');
    await preencher(page);
    await page.getByRole('button', { name: 'Entrar' }).click();

    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    await expect(page.getByTestId('nome-do-espaco')).toHaveText('Finanças de Ana');
    expect(corpo).toEqual({ email: 'ana@example.com', password: 'senha-segura' });
  });

  test('rota logada sem sessão vai ao login e volta para ela depois de entrar', async ({
    page,
  }) => {
    await page.route('**/api/v1/auth/login', (route) => route.fulfill({ json: sessao }));

    await page.goto('/perfil');
    await expect(page).toHaveURL(/\/entrar\?returnUrl=%2Fperfil$/);

    await preencher(page);
    await page.getByRole('button', { name: 'Entrar' }).click();
    await expect(page).toHaveURL(/\/perfil$/);
  });

  test('credenciais inválidas mostram um erro único e não entram', async ({ page }) => {
    await page.route('**/api/v1/auth/login', (route) =>
      route.fulfill({
        status: 401,
        contentType: 'application/problem+json',
        json: { status: 401, code: 'invalid_credentials', detail: 'E-mail ou senha incorretos' },
      }),
    );

    await page.goto('/entrar');
    await preencher(page, 'errada-123');
    await page.getByRole('button', { name: 'Entrar' }).click();

    await expect(page.getByTestId('erro-geral')).toHaveText('E-mail ou senha incorretos');
    await expect(page).toHaveURL(/\/entrar$/);
  });

  test('bloqueio mostra o tempo restante e impede nova tentativa', async ({ page }) => {
    await page.route('**/api/v1/auth/login', (route) =>
      route.fulfill({
        status: 429,
        contentType: 'application/problem+json',
        json: {
          status: 429,
          code: 'login_locked',
          detail: 'Muitas tentativas. Tente novamente em alguns minutos.',
          retryAfterSeconds: 900,
        },
      }),
    );

    await page.goto('/entrar');
    await preencher(page);
    await page.getByRole('button', { name: 'Entrar' }).click();

    await expect(page.getByTestId('bloqueio')).toContainText(/Tente novamente em\s+1[45] min/);
    await expect(page.getByRole('button', { name: 'Entrar' })).toBeDisabled();
  });

  test('tem atalhos para criar conta e recuperar senha', async ({ page }) => {
    await page.goto('/entrar');
    await page.getByRole('link', { name: 'Esqueci minha senha' }).click();
    await expect(page).toHaveURL(/\/esqueci-senha$/);
    await page.goBack();
    await page.getByRole('link', { name: 'Criar conta' }).click();
    await expect(page).toHaveURL(/\/criar-conta$/);
  });

  test('fechar e reabrir o navegador continua logado (cookie de refresh simulado)', async ({
    browser,
  }) => {
    const refreshCookie = 'finmy_rt=valido';
    const rotas = async (page: Page) => {
      await page.route('**/api/v1/auth/login', (route) =>
        route.fulfill({
          json: sessao,
          headers: { 'set-cookie': `${refreshCookie}; Path=/api/v1/auth; Max-Age=3600` },
        }),
      );
      await page.route('**/api/v1/auth/refresh', (route) =>
        route.request().headers()['cookie']?.includes(refreshCookie)
          ? route.fulfill({ json: sessao })
          : route.fulfill(semSessao),
      );
    };

    const primeiro = await browser.newContext();
    const a = await primeiro.newPage();
    await rotas(a);
    await a.goto('/entrar');
    await preencher(a);
    await a.getByRole('button', { name: 'Entrar' }).click();
    await expect(a).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    const estado = await primeiro.storageState();
    await primeiro.close();

    // "Reabre" o navegador: contexto novo, só com os cookies persistidos.
    const segundo = await browser.newContext({ storageState: estado });
    const b = await segundo.newPage();
    await rotas(b);
    await b.goto('/perfil');
    await expect(b).toHaveURL(/\/perfil$/);
    await expect(b.getByTestId('nome-do-espaco')).toHaveText('Finanças de Ana');
    await segundo.close();
  });
});

// Contra a stack real (backend + Postgres, `E2E_BASE_URL`): cadastro, fechar/reabrir o navegador e
// continuar logado (`POST /auth/refresh`, T022).
test('fecha e reabre o navegador e continua logado (stack real)', async ({ browser }) => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');
  const email = `ana+${Date.now()}@example.com`;
  const senha = 'senha-segura-1';

  const primeiro = await browser.newContext();
  const a = await primeiro.newPage();
  await a.goto('/criar-conta');
  await a.getByLabel('Nome').fill('Ana Souza');
  await a.getByLabel('E-mail').fill(email);
  await a.getByLabel('Senha', { exact: true }).fill(senha);
  await a.getByRole('button', { name: 'Criar conta' }).click();
  await expect(a).toHaveURL(/\/mes\/\d{4}-\d{2}$/);

  // Sai e entra de novo pela tela de login (cobre o POST /auth/login real).
  await primeiro.clearCookies();
  await a.goto('/entrar');
  await a.getByLabel('E-mail').fill(email);
  await a.getByLabel('Senha', { exact: true }).fill(senha);
  await a.getByRole('button', { name: 'Entrar' }).click();
  await expect(a).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
  const estado = await primeiro.storageState();
  await primeiro.close();

  const segundo = await browser.newContext({ storageState: estado });
  const b = await segundo.newPage();
  await b.goto('/perfil');
  await expect(b).toHaveURL(/\/perfil$/);
  await segundo.close();
});
