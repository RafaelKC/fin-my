import { createHash, randomBytes } from 'node:crypto';
import { expect, type APIRequestContext } from '@playwright/test';

/** Ajudantes dos e2e do conector do Claude (008 T002): o que o Claude faz, e a semeadura na stack real. */

export const CALLBACK = 'https://claude.ai/api/mcp/auth_callback';

export const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Casa da Lia', role: 'owner' },
};

function base64Url(bytes: Buffer): string {
  return bytes.toString('base64url');
}

/** Par PKCE S256, como o Claude gera. */
export function novoPkce(): { verifier: string; challenge: string } {
  const verifier = base64Url(randomBytes(32));
  return { verifier, challenge: base64Url(createHash('sha256').update(verifier).digest()) };
}

/** Registro dinâmico de cliente (RFC 7591): devolve o `client_id`. */
export async function registrarCliente(
  request: APIRequestContext,
  nome = 'Claude',
): Promise<string> {
  const resposta = await request.post('/oauth/register', {
    data: { client_name: nome, redirect_uris: [CALLBACK] },
  });
  expect(resposta.status()).toBe(201);
  return ((await resposta.json()) as { client_id: string }).client_id;
}

/** O endereço que o Claude abre no navegador da pessoa. */
export function urlDeAutorizacao(clientId: string, challenge: string, state = 'estado-1'): string {
  const consulta = new URLSearchParams({
    response_type: 'code',
    client_id: clientId,
    redirect_uri: CALLBACK,
    code_challenge: challenge,
    code_challenge_method: 'S256',
    state,
  });
  return `/oauth/authorize?${consulta.toString()}`;
}

/** Troca o código pelos tokens (como o Claude, no `/oauth/token`). */
export async function trocarCodigo(
  request: APIRequestContext,
  clientId: string,
  code: string,
  verifier: string,
): Promise<{ access_token: string; refresh_token: string }> {
  const resposta = await request.post('/oauth/token', {
    form: {
      grant_type: 'authorization_code',
      client_id: clientId,
      code,
      code_verifier: verifier,
      redirect_uri: CALLBACK,
    },
  });
  expect(resposta.status()).toBe(200);
  return (await resposta.json()) as { access_token: string; refresh_token: string };
}

/** Conecta o Claude de ponta a ponta pela API (sem tela), com o bearer do app da pessoa. Devolve o token do conector. */
export async function conectarClaude(
  request: APIRequestContext,
  tokenDoApp: string,
  nome = 'Claude',
): Promise<string> {
  const clientId = await registrarCliente(request, nome);
  const { verifier, challenge } = novoPkce();
  const ticket = await request.post('/api/v1/oauth/consent-tickets', {
    headers: { Authorization: `Bearer ${tokenDoApp}` },
    data: { clientId, redirectUri: CALLBACK, codeChallenge: challenge },
  });
  expect(ticket.status()).toBe(201);
  const { ticket: valor } = (await ticket.json()) as { ticket: string };
  const autorizar = await request.get(
    `${urlDeAutorizacao(clientId, challenge)}&consent_ticket=${valor}`,
    { maxRedirects: 0 },
  );
  expect(autorizar.status()).toBe(302);
  const code = new URL(autorizar.headers()['location']).searchParams.get('code');
  expect(code).toBeTruthy();
  return (await trocarCodigo(request, clientId, code!, verifier)).access_token;
}

/** Cria uma conta pela API (Lia), dispensa o guia de primeiros passos e devolve o e-mail, a senha e o token do app. */
export async function criarConta(
  request: APIRequestContext,
  nome = 'Lia',
): Promise<{ email: string; senha: string; token: string }> {
  const email = `lia+${Date.now()}-${Math.round(Math.random() * 1e6)}@example.com`;
  const senha = 'senha-segura-123';
  const resposta = await request.post('/api/v1/auth/signup', {
    data: { name: nome, email, password: senha },
  });
  expect(resposta.status()).toBe(201);
  const token = ((await resposta.json()) as { accessToken: string }).accessToken;
  // Espaço sem carteiras abriria o guia de primeiros passos (006 FR-007) depois do login pela tela.
  const dispensa = await request.post('/api/v1/onboarding/dismiss', {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(dispensa.ok()).toBe(true);
  return { email, senha, token };
}

/** Chamada ao `/mcp` com o token do conector: 401 quando a autorização foi desconectada. */
export async function statusDoMcp(request: APIRequestContext, token: string): Promise<number> {
  const resposta = await request.post('/mcp', {
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      Accept: 'application/json, text/event-stream',
    },
    data: {
      jsonrpc: '2.0',
      id: 1,
      method: 'initialize',
      params: {
        protocolVersion: '2025-03-26',
        capabilities: {},
        clientInfo: { name: 'e2e', version: '1' },
      },
    },
  });
  return resposta.status();
}
