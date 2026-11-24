import { expect, type Page } from '@playwright/test';

/** Dados e ajudantes dos e2e de tags (004 T004): API simulada e semeadura na stack real. */

export const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};

/** "Hoje" fixo: 10/03/2026. */
export const HOJE = '2026-03-10T12:00:00-03:00';

export const carteiraAzul = {
  id: 'w-azul',
  name: 'Azul',
  type: 'checking',
  color: '#1E88E5',
  initialBalanceCents: 50000,
  initialBalanceDate: '2026-03-01',
  isPrimary: true,
  archived: false,
  currentBalanceCents: 50000,
  hasEntries: false,
  version: 1,
};

export interface TagSimulada {
  id: string;
  name: string;
  color: string;
  isSettlement: boolean;
  archived: boolean;
  uses90d: number;
  inUse: boolean;
  version: number;
}

export function tag(id: string, name: string, uses90d: number, isSettlement = false): TagSimulada {
  return {
    id,
    name,
    color: '#3E63DD',
    isSettlement,
    archived: false,
    uses90d,
    inUse: uses90d > 0,
    version: 1,
  };
}

/** Bruna e Carla são tags de acerto; as demais, comuns (nomes fictícios). */
export const TAGS = [
  tag('t-bruna', 'Bruna', 30, true),
  tag('t-carla', 'Carla', 4, true),
  tag('t-ifood', 'Ifood', 20),
  tag('t-mercado', 'Mercado', 12),
  tag('t-uber', 'Uber', 9),
  tag('t-casa', 'Casa', 8),
  tag('t-lazer', 'Lazer', 7),
  tag('t-saude', 'Saúde', 6),
  tag('t-pet', 'Pet', 5),
  tag('t-viagem', 'Viagem', 1),
];

export const problema = (status: number, code: string, detail: string, extra: object = {}) => ({
  status,
  contentType: 'application/problem+json',
  json: { type: 'about:blank', title: code, status, code, detail, ...extra },
});

/** Cadastra pela API (o cookie de sessão fica no contexto: a página já nasce logada). */
export async function criarEspacoReal(page: Page) {
  const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  const cadastro = await page.request.post('/api/v1/auth/signup', {
    data: { name: 'Lia Souza', email, password: 'senha-teste-123' },
  });
  expect(cadastro.ok()).toBe(true);
  const { accessToken } = await cadastro.json();
  const auth = { Authorization: `Bearer ${accessToken}` };

  const post = async <T = Record<string, unknown>>(
    caminho: string,
    data: object,
    status = 201,
  ): Promise<T> => {
    const resposta = await page.request.post(`/api/v1${caminho}`, {
      headers: { ...auth, 'Idempotency-Key': crypto.randomUUID() },
      data,
    });
    expect(resposta.status(), `POST ${caminho}: ${await resposta.text()}`).toBe(status);
    return (await resposta.json()) as T;
  };

  const carteira = await post<{ id: string }>('/wallets', {
    name: 'Azul',
    type: 'checking',
    color: '#1E88E5',
    initialBalance: '500,00',
    initialBalanceDate: '2026-03-01',
  });
  return {
    auth,
    walletId: carteira.id,
    tag: (name: string, isSettlement = false) =>
      post<{ id: string; name: string }>('/tags', { name, isSettlement }),
    cartao: (name: string, closingDay: number, dueDay: number) =>
      post<{ id: string }>('/credit-cards', {
        name,
        closingDay,
        dueDay,
        paymentWalletId: carteira.id,
        color: '#43A047',
        limit: null,
      }),
    entrada: (
      kind: 'income' | 'expense',
      description: string,
      amount: string,
      date: string,
      tagIds: string[] = [],
    ) =>
      post('/entries', {
        kind,
        description,
        amount,
        date,
        walletId: carteira.id,
        status: 'settled',
        tagIds,
      }),
    compra: (
      creditCardId: string,
      description: string,
      amount: string,
      date: string,
      tagIds: string[] = [],
    ) => post('/card-purchases', { description, amount, date, creditCardId, tagIds }),
  };
}
