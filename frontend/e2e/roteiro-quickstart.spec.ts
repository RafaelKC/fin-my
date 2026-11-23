import { expect, test, type Page } from '@playwright/test';

// Roteiro manual do quickstart §3 que ainda não tinha cobertura contra a stack real
// (passos 1, 2, 3 e 6). Os passos 4, 5 e 7 estão em `perfil.spec.ts`, `recuperar-senha.spec.ts` e
// `entrar.spec.ts`; o 8 (PWA) em `pwa.spec.ts`. Rodam nos dois viewports (mobile 360 px e desktop).
// Precisam da stack do compose no ar e de `E2E_BASE_URL`; sem ela, são ignorados.
test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (defina E2E_BASE_URL)');

const mailpit = process.env['MAILPIT_URL'] ?? 'http://localhost:8025';

async function semRolagemHorizontal(page: Page) {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  );
  expect(overflow).toBe(false);
}

async function criarConta(page: Page, nome: string, email: string, senha: string) {
  await page.goto('/criar-conta');
  await page.getByLabel('Nome').fill(nome);
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha', { exact: true }).fill(senha);
  await page.getByRole('button', { name: 'Criar conta' }).click();
}

test('passos 1 e 2: cria a conta e vê o Espaço; o mesmo e-mail em maiúsculas é recusado', async ({
  page,
}) => {
  const email = `ana+${Date.now()}@exemplo.com`;

  await criarConta(page, 'Ana', email, 'segredo123');
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByTestId('nome-do-espaco')).toHaveText('Finanças de Ana');
  await semRolagemHorizontal(page);

  // Outro aparelho/navegador tentando criar de novo, agora com o e-mail em maiúsculas.
  await page.context().clearCookies();
  await criarConta(page, 'Ana', email.toUpperCase(), 'segredo123');

  const aviso = page.getByTestId('email-ja-cadastrado');
  await expect(aviso).toContainText('Este e-mail já tem conta');
  await expect(aviso.getByRole('link', { name: 'Entrar' })).toBeVisible();
  await expect(aviso.getByRole('link', { name: 'Recuperar senha' })).toBeVisible();
  await expect(page).toHaveURL(/\/criar-conta$/);
  await semRolagemHorizontal(page);
});

test('passo 3: cinco senhas erradas e a sexta tentativa mostra o bloqueio de 15 min', async ({
  page,
}) => {
  const email = `bloqueio+${Date.now()}@exemplo.com`;
  const senha = 'segredo123';

  await criarConta(page, 'Bia', email, senha);
  await expect(page).toHaveURL(/\/$/);
  await page.context().clearCookies();

  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(email);
  for (let tentativa = 1; tentativa <= 5; tentativa++) {
    await page.getByLabel('Senha', { exact: true }).fill(`errada-${tentativa}-xyz`);
    await page.getByRole('button', { name: 'Entrar' }).click();
    await expect(page.getByTestId('erro-geral')).toHaveText('E-mail ou senha incorretos');
  }

  // 6ª tentativa: bloqueio, mesmo com a senha certa.
  await page.getByLabel('Senha', { exact: true }).fill(senha);
  await page.getByRole('button', { name: 'Entrar' }).click();

  await expect(page.getByTestId('bloqueio')).toContainText(/Tente novamente em\s+1[45] min/);
  await expect(page.getByRole('button', { name: 'Entrar' })).toBeDisabled();
  await expect(page).toHaveURL(/\/entrar$/);
  await semRolagemHorizontal(page);
});

test('passo 6: e-mail inexistente em /esqueci-senha recebe a mesma mensagem e nenhum e-mail', async ({
  page,
  request,
}) => {
  const inexistente = `ninguem+${Date.now()}@exemplo.com`;
  const existente = `carla+${Date.now()}@exemplo.com`;
  const confirmacao = 'Se houver conta com este e-mail, enviaremos um link';

  await criarConta(page, 'Carla', existente, 'segredo123');
  await expect(page).toHaveURL(/\/$/);
  await page.context().clearCookies();

  for (const email of [inexistente, existente]) {
    await page.goto('/esqueci-senha');
    await page.getByLabel('E-mail').fill(email);
    await page.getByRole('button', { name: 'Enviar link' }).click();
    // A resposta é idêntica com ou sem conta (US4.2).
    await expect(page.getByTestId('link-enviado')).toContainText(confirmacao);
  }

  const mensagensPara = async (email: string) => {
    const busca = await request.get(
      `${mailpit}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`,
    );
    return ((await busca.json()) as { messages: unknown[] }).messages.length;
  };

  // O envio é em segundo plano: esperar o e-mail da conta real (controle positivo) garante que o da
  // conta inexistente, pedido antes, já teria chegado se fosse enviado.
  await expect.poll(() => mensagensPara(existente)).toBe(1);
  expect(await mensagensPara(inexistente)).toBe(0);
});
