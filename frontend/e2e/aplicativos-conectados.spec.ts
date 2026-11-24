import { expect, test } from '@playwright/test';
import { irPara } from './navegacao';
import { conectarClaude, criarConta, sessao, statusDoMcp } from './claude-api';

// 008 US1.2 / FR-003: Perfil → Aplicativos conectados mostra o Claude, desde quando e o último uso, e desconecta na hora.

const claude = {
  id: 'auth-1',
  name: 'Claude',
  connectedAt: '2026-11-20T13:00:00Z',
  lastUsedAt: '2026-11-23T15:30:00Z',
};

test.describe('aplicativos conectados (US1.2) — API simulada', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  });

  test('lista o Claude com a data da conexão e o último uso', async ({ page }) => {
    await page.route('**/api/v1/me/connected-apps', (route) => route.fulfill({ json: [claude] }));

    await page.goto('/perfil');

    const item = page.getByTestId('aplicativo-conectado');
    await expect(item).toHaveCount(1);
    await expect(item).toContainText('Claude');
    await expect(item).toContainText('Conectado em');
    await expect(item.getByTestId('ultimo-uso')).toContainText('Último uso em');
  });

  test('desconectar pede confirmação, chama a API e tira o Claude da lista', async ({ page }) => {
    let desconectou: string | undefined;
    await page.route('**/api/v1/me/connected-apps', (route) => route.fulfill({ json: [claude] }));
    await page.route('**/api/v1/me/connected-apps/auth-1', (route) => {
      desconectou = route.request().method();
      return route.fulfill({ status: 204 });
    });

    await page.goto('/perfil');
    await page.getByRole('button', { name: 'Desconectar' }).click();
    expect(desconectou).toBeUndefined();
    await page.getByRole('button', { name: 'Sim, desconectar' }).click();

    await expect(page.getByTestId('sem-aplicativos')).toBeVisible();
    expect(desconectou).toBe('DELETE');
  });

  test('sem conexões explica como conectar', async ({ page }) => {
    await page.route('**/api/v1/me/connected-apps', (route) => route.fulfill({ json: [] }));

    await page.goto('/perfil');

    await expect(page.getByTestId('sem-aplicativos')).toContainText('adicione o conector');
  });
});

// Stack real: depois de desconectar, o token do Claude deixa de valer na próxima chamada (SC-004).
test('desconectar encerra o acesso do Claude na hora (stack real)', async ({ page, request }) => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');
  const conta = await criarConta(request);
  const tokenDoClaude = await conectarClaude(request, conta.token);
  expect(await statusDoMcp(request, tokenDoClaude)).not.toBe(401);

  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill(conta.email);
  await page.getByLabel('Senha', { exact: true }).fill(conta.senha);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
  await irPara(page, 'Perfil');

  const item = page.getByTestId('aplicativo-conectado');
  await expect(item).toHaveCount(1);
  await expect(item).toContainText('Claude');
  await expect(item.getByTestId('ultimo-uso')).toContainText('Último uso em');

  await page.getByRole('button', { name: 'Desconectar' }).click();
  await page.getByRole('button', { name: 'Sim, desconectar' }).click();
  await expect(page.getByTestId('sem-aplicativos')).toBeVisible();

  expect(await statusDoMcp(request, tokenDoClaude)).toBe(401);
});
