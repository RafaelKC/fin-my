import { expect, test, type Page } from '@playwright/test';

async function semSessao(page: Page) {
  await page.route('**/api/v1/auth/refresh', (route) =>
    route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      json: { status: 401, code: 'unauthorized', detail: 'Sessão inválida.' },
    }),
  );
}

test.describe('recuperar senha (US4) — API simulada', () => {
  test.beforeEach(async ({ page }) => {
    await semSessao(page);
  });

  test('pede o link e mostra a confirmação (202)', async ({ page }) => {
    let corpo: unknown;
    await page.route('**/api/v1/auth/password-reset/request', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({ status: 202 });
    });

    await page.goto('/esqueci-senha');
    await page.getByLabel('E-mail').fill('ana@example.com');
    await page.getByRole('button', { name: 'Enviar link' }).click();

    await expect(page.getByTestId('link-enviado')).toContainText(
      'Se houver conta com este e-mail, enviaremos um link',
    );
    expect(corpo).toEqual({ email: 'ana@example.com' });
  });

  test('e-mail inválido não chama a API', async ({ page }) => {
    let chamou = false;
    await page.route('**/api/v1/auth/password-reset/request', (route) => {
      chamou = true;
      return route.fulfill({ status: 202 });
    });

    await page.goto('/esqueci-senha');
    await page.getByLabel('E-mail').fill('sem-arroba');
    await page.getByRole('button', { name: 'Enviar link' }).click();

    await expect(page.getByText('Informe um e-mail válido.')).toBeVisible();
    expect(chamou).toBe(false);
  });

  test('redefine a senha com o token do link (204)', async ({ page }) => {
    let corpo: unknown;
    await page.route('**/api/v1/auth/password-reset/confirm', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({ status: 204 });
    });

    await page.goto('/redefinir-senha?token=tok-123');
    await page.getByLabel('Nova senha').fill('nova-senha-1');
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page.getByTestId('senha-redefinida')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/entrar');
    expect(corpo).toEqual({ token: 'tok-123', newPassword: 'nova-senha-1' });
  });

  test('link expirado ou já usado (410) mostra "Pedir novo link"', async ({ page }) => {
    await page.route('**/api/v1/auth/password-reset/confirm', (route) =>
      route.fulfill({
        status: 410,
        contentType: 'application/problem+json',
        json: { status: 410, code: 'reset_token_invalid', detail: 'Link inválido.' },
      }),
    );

    await page.goto('/redefinir-senha?token=velho');
    await page.getByLabel('Nova senha').fill('nova-senha-1');
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page.getByTestId('link-invalido')).toContainText('expirou');
    await page.getByRole('link', { name: 'Pedir novo link' }).click();
    await expect(page).toHaveURL(/\/esqueci-senha$/);
  });

  test('senha curta explica o requisito e não chama a API', async ({ page }) => {
    let chamou = false;
    await page.route('**/api/v1/auth/password-reset/confirm', (route) => {
      chamou = true;
      return route.fulfill({ status: 204 });
    });

    await page.goto('/redefinir-senha?token=tok-123');
    await page.getByLabel('Nova senha').fill('1234567');
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page.getByText('A senha precisa ter pelo menos 8 caracteres.')).toBeVisible();
    expect(chamou).toBe(false);
  });

  test('erro 422 da API aparece junto do campo', async ({ page }) => {
    await page.route('**/api/v1/auth/password-reset/confirm', (route) =>
      route.fulfill({
        status: 422,
        contentType: 'application/problem+json',
        json: {
          status: 422,
          code: 'validation_failed',
          detail: 'Dados inválidos.',
          errors: { newPassword: ['A senha é fraca demais.'] },
        },
      }),
    );

    await page.goto('/redefinir-senha?token=tok-123');
    await page.getByLabel('Nova senha').fill('nova-senha-1');
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page.getByText('A senha é fraca demais.')).toBeVisible();
  });
});

// Fluxo completo contra a API real, lendo o e-mail pela API do Mailpit.
// Pulado até a T025 (endpoints /auth/password-reset/*) entrar na main; depois
// basta trocar `test.skip` por `test` (com a stack do compose no ar).
test.skip('fluxo completo com o e-mail lido no Mailpit', async ({ page, request }) => {
  const mailpit = process.env['MAILPIT_URL'] ?? 'http://localhost:8025';
  const email = `ana+${Date.now()}@example.com`;
  const senhaAntiga = 'senha-antiga-1';
  const senhaNova = 'senha-nova-123';

  await page.goto('/criar-conta');
  await page.getByLabel('Nome').fill('Ana Souza');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha', { exact: true }).fill(senhaAntiga);
  await page.getByRole('button', { name: 'Criar conta' }).click();
  await expect(page).toHaveURL(/\/$/);

  await page.context().clearCookies(); // encerra a sessão do refresh token
  await page.goto('/esqueci-senha');
  await page.getByLabel('E-mail').fill(email);
  await page.getByRole('button', { name: 'Enviar link' }).click();
  await expect(page.getByTestId('link-enviado')).toBeVisible();

  let link = '';
  await expect
    .poll(async () => {
      const busca = await request.get(
        `${mailpit}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`,
      );
      const { messages } = (await busca.json()) as { messages: { ID: string }[] };
      if (messages.length === 0) return '';
      const msg = await request.get(`${mailpit}/api/v1/message/${messages[0].ID}`);
      const { Text } = (await msg.json()) as { Text: string };
      link = /https?:\/\/\S+\/redefinir-senha\?token=\S+/.exec(Text)?.[0] ?? '';
      return link;
    })
    .not.toBe('');

  await page.goto(link);
  await page.getByLabel('Nova senha').fill(senhaNova);
  await page.getByRole('button', { name: 'Salvar' }).click();
  await expect(page.getByTestId('senha-redefinida')).toBeVisible();

  // Link de uso único: usar de novo → expirado.
  await page.goto(link);
  await page.getByLabel('Nova senha').fill('outra-senha-123');
  await page.getByRole('button', { name: 'Salvar' }).click();
  await expect(page.getByTestId('link-invalido')).toBeVisible();
});
