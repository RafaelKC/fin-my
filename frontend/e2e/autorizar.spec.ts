import { expect, test, type Page } from '@playwright/test';
import {
  CALLBACK,
  criarConta,
  novoPkce,
  registrarCliente,
  sessao,
  trocarCodigo,
  urlDeAutorizacao,
} from './claude-api';

// 008 US1.1: o Claude leva a pessoa a /autorizar; ela entra, vê o consentimento com o nome do Espaço e autoriza ou cancela.

const semSessao = {
  status: 401,
  contentType: 'application/problem+json',
  json: { status: 401, code: 'unauthorized', detail: 'Sessão inválida.' },
};

const PEDIDO = `client_id=cliente-1&redirect_uri=${encodeURIComponent(CALLBACK)}&code_challenge=abc123-challenge&code_challenge_method=S256&response_type=code&state=estado-1`;

/** O Claude não existe no teste: o que importa é para onde o navegador é mandado depois. */
async function simularCallbackDoClaude(page: Page): Promise<void> {
  await page.route('https://claude.ai/**', (route) =>
    route.fulfill({ contentType: 'text/html', body: '<h1>Claude</h1>' }),
  );
}

/**
 * Stack real: o `/oauth/authorize` responde de verdade, mas o 302 para o Claude é capturado (o Playwright não roteia
 * redirecionamentos do servidor e o teste não deve sair para a internet). Devolve onde o navegador seria mandado.
 */
async function capturarRetornoParaOClaude(page: Page): Promise<() => URL | undefined> {
  let retorno: URL | undefined;
  await page.route('**/oauth/authorize**', async (route) => {
    const resposta = await route.fetch({ maxRedirects: 0 });
    const destino = resposta.headers()['location'];
    if (destino?.startsWith(CALLBACK)) {
      retorno = new URL(destino);
      return route.fulfill({ contentType: 'text/html', body: '<h1>Claude</h1>' });
    }
    return route.fulfill({ response: resposta });
  });
  return () => retorno;
}

test.describe('autorizar (US1.1) — API simulada', () => {
  test.beforeEach(async ({ page }) => {
    await simularCallbackDoClaude(page);
  });

  test('sem login vai ao /entrar e, depois de entrar, volta ao consentimento com o nome do Espaço', async ({
    page,
  }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill(semSessao));
    await page.route('**/api/v1/auth/login', (route) => route.fulfill({ json: sessao }));

    await page.goto(`/autorizar?${PEDIDO}`);
    await expect(page).toHaveURL(/\/entrar\?returnUrl=/);

    await page.getByLabel('E-mail').fill('lia@example.com');
    await page.getByLabel('Senha', { exact: true }).fill('senha-segura');
    await page.getByRole('button', { name: 'Entrar' }).click();

    await expect(page).toHaveURL(/\/autorizar\?/);
    expect(new URL(page.url()).searchParams.get('state')).toBe('estado-1');
    await expect(page.getByTestId('texto-consentimento')).toContainText(
      'O Claude poderá ver e registrar seus lançamentos no Espaço Casa da Lia',
    );
  });

  test('Autorizar pede o ticket e navega a página inteira para o authorize com os mesmos parâmetros', async ({
    page,
  }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
    let corpo: unknown;
    await page.route('**/api/v1/oauth/consent-tickets', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({
        status: 201,
        json: { ticket: 'ticket-opaco', expiresAt: '2099-01-01T00:02:00Z' },
      });
    });
    let authorize: URL | undefined;
    await page.route('**/oauth/authorize**', (route) => {
      authorize = new URL(route.request().url());
      return route.fulfill({ contentType: 'text/html', body: '<h1>authorize</h1>' });
    });

    await page.goto(`/autorizar?${PEDIDO}`);
    await page.getByRole('button', { name: 'Autorizar' }).click();

    await expect(page).toHaveURL(/\/oauth\/authorize\?/);
    expect(corpo).toEqual({
      clientId: 'cliente-1',
      redirectUri: CALLBACK,
      codeChallenge: 'abc123-challenge',
    });
    expect(authorize?.searchParams.get('consent_ticket')).toBe('ticket-opaco');
    expect(authorize?.searchParams.get('client_id')).toBe('cliente-1');
    expect(authorize?.searchParams.get('state')).toBe('estado-1');
  });

  test('Cancelar navega a página inteira para o authorize negando, sem pedir ticket', async ({
    page,
  }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
    let pediuTicket = false;
    await page.route('**/api/v1/oauth/consent-tickets', (route) => {
      pediuTicket = true;
      return route.abort();
    });
    await page.route('**/oauth/authorize**', (route) => {
      expect(new URL(route.request().url()).searchParams.get('consent_denied')).toBe('1');
      return route.fulfill({ contentType: 'text/html', body: '<h1>authorize</h1>' });
    });

    await page.goto(`/autorizar?${PEDIDO}`);
    await page.getByRole('button', { name: 'Cancelar' }).click();

    await expect(page).toHaveURL(/\/oauth\/authorize\?.*consent_denied=1/);
    expect(pediuTicket).toBe(false);
  });

  test('finmy-android: consentimento com título e texto do app, e Autorizar pede o ticket do cliente', async ({
    page,
  }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
    let corpo: { clientId?: string } | undefined;
    await page.route('**/api/v1/oauth/consent-tickets', (route) => {
      corpo = route.request().postDataJSON();
      return route.fulfill({
        status: 201,
        json: { ticket: 'ticket-opaco', expiresAt: '2099-01-01T00:02:00Z' },
      });
    });
    await page.route('**/oauth/authorize**', (route) =>
      route.fulfill({ contentType: 'text/html', body: '<h1>authorize</h1>' }),
    );

    const pedido = `client_id=finmy-android&redirect_uri=${encodeURIComponent('br.app.finmy:/oauth2redirect')}&code_challenge=abc123-challenge&code_challenge_method=S256&response_type=code&state=estado-1`;
    await page.goto(`/autorizar?${pedido}`);

    await expect(
      page.getByRole('heading', { name: 'Autorizar o App Fin-My (Android)' }),
    ).toBeVisible();
    await expect(page.getByTestId('texto-consentimento')).toContainText(
      'O App Fin-My (Android) poderá ver seus cartões e faturas no Espaço Casa da Lia para mostrar no widget.',
    );
    await expect(page.getByText('Claude')).toHaveCount(0);

    await page.getByRole('button', { name: 'Autorizar' }).click();
    await expect(page).toHaveURL(/\/oauth\/authorize\?.*consent_ticket=ticket-opaco/);
    expect(corpo?.clientId).toBe('finmy-android');
  });

  test('pedido sem os parâmetros do OAuth não oferece autorizar', async ({ page }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));

    await page.goto('/autorizar');

    await expect(page.getByTestId('pedido-invalido')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Autorizar' })).toHaveCount(0);
  });
});

// Fluxo completo na stack real: /oauth/authorize → /autorizar → login → Autorizar → código no redirect → token.
test('Claude abre o authorize, a pessoa entra e autoriza, e o código vira token (stack real)', async ({
  page,
  request,
}) => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');
  const retornoParaOClaude = await capturarRetornoParaOClaude(page);
  const conta = await criarConta(request);
  const clientId = await registrarCliente(request);
  const { verifier, challenge } = novoPkce();

  await page.goto(urlDeAutorizacao(clientId, challenge));
  await expect(page).toHaveURL(/\/entrar\?returnUrl=/);
  await page.getByLabel('E-mail').fill(conta.email);
  await page.getByLabel('Senha', { exact: true }).fill(conta.senha);
  await page.getByRole('button', { name: 'Entrar' }).click();

  await expect(page.getByTestId('texto-consentimento')).toContainText(
    'O Claude poderá ver e registrar seus lançamentos no Espaço',
  );
  await page.getByRole('button', { name: 'Autorizar' }).click();

  await expect.poll(retornoParaOClaude).toBeDefined();
  const retorno = retornoParaOClaude()!;
  expect(retorno.searchParams.get('state')).toBe('estado-1');
  const code = retorno.searchParams.get('code');
  expect(code).toBeTruthy();
  const tokens = await trocarCodigo(request, clientId, code!, verifier);
  expect(tokens.access_token).toBeTruthy();
});

test('Cancelar na stack real devolve access_denied ao redirect do Claude', async ({
  page,
  request,
}) => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');
  const retornoParaOClaude = await capturarRetornoParaOClaude(page);
  const conta = await criarConta(request);
  const clientId = await registrarCliente(request);
  const { challenge } = novoPkce();

  await page.goto(urlDeAutorizacao(clientId, challenge));
  await page.getByLabel('E-mail').fill(conta.email);
  await page.getByLabel('Senha', { exact: true }).fill(conta.senha);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.getByRole('button', { name: 'Cancelar' }).click();

  await expect.poll(retornoParaOClaude).toBeDefined();
  const retorno = retornoParaOClaude()!;
  expect(retorno.searchParams.get('error')).toBe('access_denied');
  expect(retorno.searchParams.get('state')).toBe('estado-1');
  expect(retorno.searchParams.get('code')).toBeNull();
});
