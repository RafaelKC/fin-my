import { expect, test, type Page } from '@playwright/test';
import { HOJE, TAGS, carteiraAzul, criarEspacoReal, sessao } from './tags-api';

// 004 T004 — filtros por tag na visão do mês, na fatura e na busca (US1.5; FR-004; research R6).
// A API simulada responde conforme `tagIds`; a stack real confere o filtro e os totais no servidor.

const coluna = {
  openingBalanceCents: 50000,
  walletsInitialBalanceCents: 50000,
  incomeCents: 3000,
  expenseCents: 12000,
  resultCents: -9000,
  transfersInCents: 0,
  transfersOutCents: 0,
  finalBalanceCents: 41000,
};

const entrada = (
  id: string,
  kind: string,
  description: string,
  cents: number,
  tagIds: string[],
) => ({
  id,
  kind,
  description,
  amountCents: cents,
  date: '2026-03-05',
  referenceMonth: '2026-03',
  status: 'settled',
  walletId: 'w-azul',
  createdAt: '',
  version: 1,
  tagIds,
  settlementTagId: tagIds.includes('t-bruna') ? 't-bruna' : null,
});

const compra = (id: string, description: string, cents: number, tagIds: string[]) => ({
  id,
  creditCardId: 'c-verde',
  dueMonth: '2026-03',
  description,
  displayDescription: description,
  amountCents: cents,
  nature: 'debit',
  date: '2026-03-04',
  origin: 'manual',
  reconciled: false,
  manuallyMoved: false,
  notes: null,
  installmentPlanId: null,
  installmentNumber: null,
  installmentTotal: null,
  createdAt: '',
  version: 1,
  tagIds,
  settlementTagId: tagIds.includes('t-bruna') ? 't-bruna' : null,
});

const presente = entrada('e1', 'expense', 'Presente Bruna', 4000, ['t-bruna']);
const mercado = entrada('e2', 'expense', 'Mercado', 8000, ['t-mercado']);
const pix = entrada('e3', 'income', 'Bruna Pix', 3000, ['t-bruna']);
const ifood = compra('p1', 'Ifood', 15681, ['t-bruna', 't-ifood']);
const uber = compra('p2', 'Uber', 1272, ['t-uber']);

const cartaoVerde = {
  id: 'c-verde',
  name: 'Verde',
  closingDay: 18,
  dueDay: 25,
  paymentWalletId: 'w-azul',
  color: '#43A047',
  limitCents: null,
  archived: false,
  hasPurchases: true,
  version: 1,
  currentInvoice: fatura(),
  nextInvoice: { ...fatura(), dueMonth: '2026-04' },
  openPastInvoices: [],
  availableLimitCents: null,
};

function fatura() {
  return {
    creditCardId: 'c-verde',
    dueMonth: '2026-03',
    closingDate: '2026-03-18',
    dueDate: '2026-03-25',
    id: 'f1',
    status: 'closed',
    totalCents: 16953,
    paidCents: 0,
    remainingCents: 16953,
  };
}

async function simular(page: Page): Promise<string[]> {
  const consultas: string[] = [];
  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteiraAzul] }));
  await page.route('**/api/v1/tags**', (route) => route.fulfill({ json: TAGS }));
  await page.route('**/api/v1/credit-cards', (route) => route.fulfill({ json: [cartaoVerde] }));
  await page.route('**/api/v1/credit-cards/c-verde', (route) =>
    route.fulfill({ json: cartaoVerde }),
  );
  await page.route('**/api/v1/months/2026-03**', (route) => {
    const url = new URL(route.request().url());
    consultas.push(url.search);
    const filtrado = url.searchParams.get('tagIds') === 't-bruna';
    return route.fulfill({
      json: {
        month: '2026-03',
        today: '2026-03-10',
        walletIds: null,
        planned: coluna,
        actual: coluna,
        entries: filtrado ? [presente, pix] : [presente, mercado, pix],
        transfers: [],
        invoices: filtrado
          ? []
          : [{ ...fatura(), creditCardName: 'Verde', plannedCents: 16953, actualCents: 0 }],
        pending: { count: 0, toReceiveCents: 0, toPayCents: 0 },
        purchases: filtrado ? [ifood] : [],
        filterTotals: filtrado
          ? { incomeCents: 3000, expenseCents: 4000, purchasesNetCents: 15681 }
          : null,
      },
    });
  });
  await page.route('**/api/v1/credit-cards/c-verde/invoices/2026-03**', (route) => {
    const url = new URL(route.request().url());
    consultas.push(url.search);
    const filtrado = url.searchParams.get('tagIds') === 't-bruna';
    return route.fulfill({
      json: {
        ...fatura(),
        reconciledCents: 0,
        canCarryOver: false,
        purchases: filtrado ? [ifood] : [ifood, uber],
        payments: [],
        previousMonth: '2026-02',
        nextMonth: '2026-04',
        filteredTotalCents: filtrado ? 15681 : null,
        settlementSubtotals: [],
      },
    });
  });
  await page.route('**/api/v1/entries/search**', (route) => {
    const url = new URL(route.request().url());
    consultas.push(url.search);
    const filtrado = url.searchParams.get('tagIds') === 't-bruna';
    const itens = filtrado
      ? [
          { type: 'expense', entry: presente },
          { type: 'income', entry: pix },
          { type: 'card_purchase', purchase: ifood },
        ]
      : [
          { type: 'expense', entry: presente },
          { type: 'expense', entry: mercado },
          { type: 'income', entry: pix },
          { type: 'card_purchase', purchase: ifood },
          { type: 'card_purchase', purchase: uber },
        ];
    return route.fulfill({
      json: { items: itens, totalCents: filtrado ? -16681 : -26953, totalCount: itens.length },
    });
  });
  return consultas;
}

const abrirFiltro = async (page: Page) => {
  await page.getByTestId('filtro-tags-abrir').click();
  await expect(page.getByTestId('filtro-tag').first()).toBeVisible();
};
const escolherTag = (page: Page, nome: string) =>
  page.locator(`[data-testid="filtro-tag"][data-tag="${nome}"]`).click();

test.describe('filtros por tag (US1.5) — API simulada', () => {
  test('visão do mês: filtra por "Bruna", mostra as compras do cartão e os totais do filtro', async ({
    page,
  }) => {
    const consultas = await simular(page);
    await page.goto('/mes/2026-03');
    await expect(page.getByTestId('lancamento')).toHaveCount(3);
    await expect(page.getByTestId('totais-filtro')).toHaveCount(0);

    await abrirFiltro(page);
    await escolherTag(page, 'Bruna');

    await expect(page).toHaveURL(/tags=t-bruna/);
    await expect(page.getByTestId('lancamento')).toHaveCount(2);
    await expect(page.getByTestId('compra-filtro')).toHaveCount(1);
    await expect(page.getByTestId('compra-filtro')).toContainText('Ifood');
    await expect(page.getByTestId('filtro-entradas')).toHaveText('R$ 30,00');
    await expect(page.getByTestId('filtro-saidas')).toHaveText('R$ 40,00');
    await expect(page.getByTestId('filtro-compras')).toHaveText('R$ 156,81');
    // o resumo continua o do mês inteiro (research R6)
    await expect(page.locator('[data-testid="resumo-saidas"] [data-testid="previsto"]')).toHaveText(
      'R$ 120,00',
    );
    expect(consultas.some((c) => c.includes('tagIds=t-bruna'))).toBe(true);

    // o filtro vem da URL: recarregar mantém
    await page.reload();
    await expect(page.getByTestId('lancamento')).toHaveCount(2);

    await abrirFiltro(page);
    await page.getByTestId('filtro-tags-limpar').click();
    await expect(page.getByTestId('lancamento')).toHaveCount(3);
    await expect(page.getByTestId('compra-filtro')).toHaveCount(0);
  });

  test('fatura: filtra as compras por "Bruna" e mostra o total filtrado', async ({ page }) => {
    await simular(page);
    await page.goto('/cartoes/c-verde/faturas/2026-03');
    await expect(page.getByTestId('compra')).toHaveCount(2);

    await abrirFiltro(page);
    await escolherTag(page, 'Bruna');
    await expect(page.getByTestId('compra')).toHaveCount(1);
    await expect(page.getByTestId('total-filtrado')).toContainText('R$ 156,81');
    // o Total da fatura segue o da fatura inteira
    await expect(page.getByTestId('total')).toContainText('R$ 169,53');
  });

  test('busca: filtra por "Bruna" (entradas, saídas e compras no cartão)', async ({ page }) => {
    await simular(page);
    await page.goto('/busca');
    await expect(page.getByTestId('total')).toContainText('5 resultados');

    await abrirFiltro(page);
    await escolherTag(page, 'Bruna');
    await expect(page.getByTestId('total')).toContainText('3 resultados');
    await expect(page.getByTestId('resultado')).toHaveCount(2);
    await expect(page.getByTestId('resultado-compra')).toContainText('Ifood');

    await page.getByTestId('limpar').click();
    await expect(page.getByTestId('total')).toContainText('5 resultados');
  });
});

// Contra a stack real (E2E_BASE_URL): o servidor filtra, junta as compras das faturas do mês e soma.
test.describe('filtros por tag (US1.5) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  test('US1.5: filtrar por "Bruna" no mês, na fatura e na busca', async ({ page }) => {
    const api = await criarEspacoReal(page);
    const bruna = await api.tag('Bruna', true);
    const mercadoTag = await api.tag('Mercado');
    const ifoodTag = await api.tag('Ifood');
    const verde = await api.cartao('Verde', 18, 25);

    await api.entrada('expense', 'Presente Bruna', '40,00', '2026-03-05', [bruna.id]);
    await api.entrada('expense', 'Compra Mercado', '80,00', '2026-03-06', [mercadoTag.id]);
    await api.entrada('income', 'Bruna Pix', '30,00', '2026-03-07', [bruna.id]);
    await api.compra(verde.id, 'Ifood', '156,81', '2026-03-10', [bruna.id, ifoodTag.id]);
    await api.compra(verde.id, 'Uber', '12,72', '2026-03-11', [mercadoTag.id]);

    // mês
    await page.goto('/mes/2026-03');
    await expect(page.getByTestId('lancamento')).toHaveCount(3);
    const resumoAntes = await page
      .locator('[data-testid="resumo-saidas"] [data-testid="previsto"]')
      .textContent();
    await abrirFiltro(page);
    await escolherTag(page, 'Bruna');
    await expect(page.getByTestId('lancamento')).toHaveCount(2);
    await expect(page.getByTestId('compra-filtro')).toHaveCount(1);
    await expect(page.getByTestId('compra-filtro')).toContainText('Ifood');
    await expect(page.getByTestId('filtro-entradas')).toHaveText('R$ 30,00');
    await expect(page.getByTestId('filtro-saidas')).toHaveText('R$ 40,00');
    await expect(page.getByTestId('filtro-compras')).toHaveText('R$ 156,81');
    await expect(page.locator('[data-testid="resumo-saidas"] [data-testid="previsto"]')).toHaveText(
      resumoAntes ?? '',
    );

    // fatura
    await page.goto(`/cartoes/${verde.id}/faturas/2026-03`);
    await expect(page.getByTestId('compra')).toHaveCount(2);
    await abrirFiltro(page);
    await escolherTag(page, 'Bruna');
    await expect(page.getByTestId('compra')).toHaveCount(1);
    await expect(page.getByTestId('total-filtrado')).toContainText('R$ 156,81');
    await expect(page.getByTestId('total')).toContainText('R$ 169,53');

    // busca
    await page.goto('/busca');
    await expect(page.getByTestId('total')).toContainText('5 resultados');
    await abrirFiltro(page);
    await escolherTag(page, 'Bruna');
    await expect(page.getByTestId('total')).toContainText('3 resultados');
    await expect(page.getByTestId('resultado')).toHaveCount(2);
    await expect(page.getByTestId('resultado-compra')).toHaveCount(1);
  });
});
