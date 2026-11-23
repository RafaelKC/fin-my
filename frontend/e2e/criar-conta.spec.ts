import { expect, test } from '@playwright/test';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Ana Souza', email: 'ana@example.com' },
  tenant: { id: 't1', name: 'Finanças de Ana', role: 'owner' },
};

test.describe('criar conta (US1)', () => {
  test.beforeEach(async ({ page }) => {
    // Sem sessão ao abrir o app.
    await page.route('**/api/v1/auth/refresh', (route) =>
      route.fulfill({
        status: 401,
        contentType: 'application/problem+json',
        json: { status: 401, code: 'unauthorized', detail: 'Sessão inválida.' },
      }),
    );
    await page.goto('/criar-conta');
  });

  async function preencher(page: import('@playwright/test').Page, senha = 'senha-segura') {
    await page.getByLabel('Nome').fill('Ana Souza');
    await page.getByLabel('E-mail').fill('ana@example.com');
    await page.getByLabel('Senha', { exact: true }).fill(senha);
  }

  test('cria a conta e entra no app com o Espaço da pessoa', async ({ page }) => {
    let corpo: unknown;
    await page.route('**/api/v1/auth/signup', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({ status: 201, json: sessao });
    });

    await preencher(page);
    await page.getByRole('button', { name: 'Criar conta' }).click();

    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByTestId('nome-do-espaco')).toHaveText('Finanças de Ana');
    expect(corpo).toEqual({
      name: 'Ana Souza',
      email: 'ana@example.com',
      password: 'senha-segura',
    });
  });

  test('mostrar/ocultar senha', async ({ page }) => {
    const senha = page.getByLabel('Senha', { exact: true });
    await senha.fill('segredo123');
    await expect(senha).toHaveAttribute('type', 'password');

    await page.getByRole('button', { name: 'Mostrar senha' }).click();
    await expect(senha).toHaveAttribute('type', 'text');

    await page.getByRole('button', { name: 'Ocultar senha' }).click();
    await expect(senha).toHaveAttribute('type', 'password');
  });

  test('senha com menos de 8 caracteres explica o requisito e não chama a API', async ({
    page,
  }) => {
    let chamou = false;
    await page.route('**/api/v1/auth/signup', (route) => {
      chamou = true;
      return route.fulfill({ status: 201, json: sessao });
    });

    await preencher(page, '1234567');
    await page.getByRole('button', { name: 'Criar conta' }).click();

    await expect(page.getByText('A senha precisa ter pelo menos 8 caracteres.')).toBeVisible();
    await expect(page).toHaveURL(/\/criar-conta$/);
    expect(chamou).toBe(false);
  });

  test('e-mail já cadastrado mostra a mensagem do cenário 1.2 com atalhos', async ({ page }) => {
    await page.route('**/api/v1/auth/signup', (route) =>
      route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        json: {
          status: 409,
          code: 'email_already_registered',
          detail: 'Este e-mail já tem conta. Entrar ou recuperar senha?',
        },
      }),
    );

    await preencher(page);
    await page.getByRole('button', { name: 'Criar conta' }).click();

    const aviso = page.getByTestId('email-ja-cadastrado');
    await expect(aviso).toContainText('Este e-mail já tem conta. Entrar ou recuperar senha?');
    await expect(aviso.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/entrar');
    await expect(aviso.getByRole('link', { name: 'Recuperar senha' })).toHaveAttribute(
      'href',
      '/esqueci-senha',
    );
    await expect(page).toHaveURL(/\/criar-conta$/);
  });

  test('erro 422 da API aparece junto do campo', async ({ page }) => {
    await page.route('**/api/v1/auth/signup', (route) =>
      route.fulfill({
        status: 422,
        contentType: 'application/problem+json',
        json: {
          status: 422,
          code: 'validation_failed',
          detail: 'Dados inválidos.',
          errors: { email: ['Informe um e-mail válido.'] },
        },
      }),
    );

    await preencher(page);
    await page.getByRole('button', { name: 'Criar conta' }).click();

    await expect(page.getByText('Informe um e-mail válido.')).toBeVisible();
  });

  test('layout sem rolagem horizontal e alvos de toque ≥ 44 px', async ({ page }) => {
    const larguraOk = await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    );
    expect(larguraOk).toBe(true);

    for (const alvo of [
      page.getByRole('button', { name: 'Criar conta' }),
      page.getByRole('button', { name: 'Mostrar senha' }),
      page.getByRole('link', { name: 'Entrar' }),
    ]) {
      const caixa = await alvo.boundingBox();
      expect(caixa!.height).toBeGreaterThanOrEqual(44);
      expect(caixa!.width).toBeGreaterThanOrEqual(44);
    }
  });
});
