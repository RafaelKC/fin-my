import { expect, type Page, type Route } from '@playwright/test';

/** Dados e API simulada da aba Cartão (003 T007), compartilhados por `compra` e `parcelas`. */

export const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};

/** "Hoje" fixo do cenário da spec (US2.1): 17/03/2026. */
export const HOJE = '2026-03-17T12:00:00-03:00';

const fatura = (id: string, mes: string, fecha: string, vence: string) => ({
  creditCardId: id,
  dueMonth: mes,
  closingDate: fecha,
  dueDate: vence,
  id: null,
  status: 'no_amount_due',
  totalCents: 0,
  paidCents: 0,
  remainingCents: 0,
});

const cartao = (id: string, name: string, closingDay: number, dueDay: number, color: string) => ({
  id,
  name,
  closingDay,
  dueDay,
  paymentWalletId: 'w-azul',
  color,
  limitCents: null,
  archived: false,
  hasPurchases: false,
  version: 1,
  currentInvoice: fatura(id, '2026-03', '2026-03-18', '2026-03-25'),
  nextInvoice: fatura(id, '2026-04', '2026-04-18', '2026-04-25'),
  openPastInvoices: [],
  availableLimitCents: null,
});

/** Verde vem antes de Azul na resposta: o padrão é o primeiro em ordem alfabética (FR-010). */
export const cartoes = [
  cartao('c-verde', 'Verde', 1, 10, '#43A047'),
  cartao('c-azul', 'Azul', 18, 25, '#1E88E5'),
];

const carteira = {
  id: 'w-azul',
  name: 'Azul',
  type: 'checking',
  color: '#1E88E5',
  initialBalanceCents: 0,
  initialBalanceDate: '2026-03-01',
  isPrimary: true,
  archived: false,
  currentBalanceCents: 0,
  hasEntries: false,
  version: 1,
};

export interface CorpoCompra {
  description: string;
  amount: string;
  date: string;
  creditCardId: string;
  notes?: string;
  confirmPaidInvoice?: boolean;
  installments?: {
    total: number;
    mode: 'total_amount' | 'installment_amount' | 'in_progress';
    nextNumber?: number;
    firstInvoiceMonth?: string;
  };
}

export interface EnvioCompra {
  corpo: CorpoCompra;
  chave: string | undefined;
}

interface Opcoes {
  /** Respostas de `GET /entries/suggestions?kind=card_purchase` por texto digitado. */
  sugestoes?: Record<string, object[]>;
  /** Quantos primeiros POST respondem 409 de fatura paga (R5). */
  faturaPaga?: number;
}

export async function simularCompras(page: Page, opcoes: Opcoes = {}): Promise<EnvioCompra[]> {
  const envios: EnvioCompra[] = [];
  let paga = opcoes.faturaPaga ?? 0;

  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteira] }));
  await page.route('**/api/v1/credit-cards**', (route) => route.fulfill({ json: cartoes }));
  await page.route('**/api/v1/entries/suggestions**', (route) => {
    const url = new URL(route.request().url());
    expect(url.searchParams.get('kind')).toBe('card_purchase');
    const q = url.searchParams.get('q') ?? '';
    return route.fulfill({ json: opcoes.sugestoes?.[q.toLowerCase()] ?? [] });
  });
  await page.route('**/api/v1/card-purchases', async (route: Route) => {
    const req = route.request();
    if (req.method() !== 'POST') return route.fulfill({ json: [] });
    const corpo = req.postDataJSON() as CorpoCompra;
    if (paga > 0) {
      paga--;
      return route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        json: {
          type: 'about:blank',
          title: 'conflict',
          status: 409,
          code: 'card_purchase.invoice_paid_confirmation_required',
          detail: 'Esta fatura já foi paga e voltará a ter valor em aberto',
        },
      });
    }
    envios.push({ corpo, chave: req.headers()['idempotency-key'] });
    return route.fulfill({
      status: 201,
      json: {
        purchases: [],
        warnings: corpo.amount.trim().startsWith('-') ? ['recorded_as_credit'] : [],
      },
    });
  });
  return envios;
}
