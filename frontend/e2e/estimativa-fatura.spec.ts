import { expect, test, type Page } from '@playwright/test';

// 007 T006 — Estimativa de fatura na tela da fatura e na linha de fatura do mês (US2.1, US2.3).
// Exemplo da US2: cartão Azul (fecha 18, vence 25), fatura 2027-05 com estimativa R$ 800,00, Total
// lançado R$ 205,53. A API simulada aplica a regra: antes do fechamento o Previsto é o maior entre
// Total, Pago e Estimativa; depois do fechamento a estimativa só aparece como referência.

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};

const carteira = {
  id: 'w-azul',
  name: 'Azul',
  type: 'checking',
  color: '#1E88E5',
  initialBalanceCents: 0,
  initialBalanceDate: '2027-01-01',
  isPrimary: true,
  archived: false,
  currentBalanceCents: 200000,
  hasEntries: false,
  version: 1,
};

const coluna = (despesa: number) => ({
  openingBalanceCents: 200000,
  walletsInitialBalanceCents: 0,
  incomeCents: 0,
  expenseCents: despesa,
  resultCents: -despesa,
  transfersInCents: 0,
  transfersOutCents: 0,
  finalBalanceCents: 200000 - despesa,
});

const TOTAL = 20553;

async function simular(page: Page, hoje: string) {
  let estimativa: number | null = null;
  const ativa = () => estimativa !== null && hoje < '2027-05-18';
  const previsto = () => (ativa() ? Math.max(TOTAL, estimativa!) : TOTAL);
  const fatura = () => ({
    creditCardId: 'c-azul',
    dueMonth: '2027-05',
    closingDate: '2027-05-18',
    dueDate: '2027-05-25',
    id: 'f1',
    status: hoje < '2027-05-18' ? 'open' : 'closed',
    totalCents: TOTAL,
    paidCents: 0,
    remainingCents: TOTAL,
    estimateCents: estimativa,
    estimateActive: ativa(),
  });
  const cartao = {
    id: 'c-azul',
    name: 'Azul',
    closingDay: 18,
    dueDay: 25,
    paymentWalletId: 'w-azul',
    color: '#1E88E5',
    limitCents: null,
    archived: false,
    hasPurchases: true,
    version: 1,
    currentInvoice: fatura(),
    nextInvoice: fatura(),
    openPastInvoices: [],
    availableLimitCents: null,
  };

  await page.clock.setFixedTime(`${hoje}T12:00:00-03:00`);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteira] }));
  await page.route('**/api/v1/entries/suggestions**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/tags**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/months/2027-05**', (route) =>
    route.fulfill({
      json: {
        month: '2027-05',
        today: hoje,
        walletIds: null,
        planned: coluna(previsto()),
        actual: coluna(0),
        entries: [],
        transfers: [],
        invoices: [
          { ...fatura(), creditCardName: 'Azul', plannedCents: previsto(), actualCents: 0 },
        ],
        pending: { count: 0, toReceiveCents: 0, toPayCents: 0 },
      },
    }),
  );
  await page.route('**/api/v1/credit-cards**', async (route) => {
    const req = route.request();
    const partes = new URL(req.url()).pathname.split('/'); // '', api, v1, credit-cards, id, invoices, mes, acao
    const [, , , , id, , , acao] = partes;
    if (!id) return route.fulfill({ json: [{ ...cartao, currentInvoice: fatura() }] });
    if (!acao && partes.length === 5) return route.fulfill({ json: cartao });
    if (acao === 'estimate') {
      const valor = (req.postDataJSON() as { amount: string | null }).amount;
      estimativa = valor === null ? null : Math.round(Number(valor.replace(',', '.')) * 100);
    }
    return route.fulfill({
      json: {
        ...fatura(),
        reconciledCents: 0,
        canCarryOver: false,
        purchases: [],
        payments: [],
        previousMonth: '2027-04',
        nextMonth: '2027-06',
      },
    });
  });
}

test.describe('estimativa de fatura (US2.1, US2.3) — API simulada', () => {
  test('US2.1: define a estimativa na fatura e a linha do mês mostra "Previsto … — lançado …"', async ({
    page,
  }) => {
    await simular(page, '2027-01-10');
    await page.goto('/cartoes/c-azul/faturas/2027-05');
    await expect(page.getByTestId('estimativa')).toContainText('Sem estimativa');

    await page.getByTestId('estimativa-editar').click();
    await page.locator('app-celula-dialog input').fill('800');
    await page.getByTestId('celula-salvar').click();
    await expect(page.getByTestId('estimativa-texto')).toContainText('Estimado R$ 800,00');
    await expect(page.getByTestId('estimativa-previsto')).toHaveText(
      'Previsto R$ 800,00 — lançado R$ 205,53',
    );
    // a estimativa não muda Total, Restante nem situação (FR-012)
    await expect(page.getByTestId('total')).toHaveText('R$ 205,53');
    await expect(page.getByTestId('restante')).toHaveText('R$ 205,53');

    await page.goto('/mes/2027-05');
    await expect(page.getByTestId('fatura-estimativa').first()).toHaveText(
      'Previsto R$ 800,00 — lançado R$ 205,53',
    );
    await expect(page.getByTestId('resumo-saidas').getByTestId('previsto')).toContainText(
      'R$ 800,00',
    );
  });

  test('remover a estimativa volta ao Previsto sem estimativa', async ({ page }) => {
    await simular(page, '2027-01-10');
    await page.goto('/cartoes/c-azul/faturas/2027-05');
    await page.getByTestId('estimativa-editar').click();
    await page.locator('app-celula-dialog input').fill('800');
    await page.getByTestId('celula-salvar').click();
    await expect(page.getByTestId('estimativa-remover')).toBeVisible();
    await page.getByTestId('estimativa-remover').click();
    await expect(page.getByTestId('estimativa')).toContainText('Sem estimativa');
    await page.goto('/mes/2027-05');
    await expect(page.getByTestId('fatura-estimativa')).toHaveCount(0);
    await expect(page.getByTestId('resumo-saidas').getByTestId('previsto')).toContainText(
      'R$ 205,53',
    );
  });

  test('US2.3: depois do fechamento a estimativa vira só referência "Estimado R$ 800,00"', async ({
    page,
  }) => {
    await simular(page, '2027-05-19');
    await page.goto('/cartoes/c-azul/faturas/2027-05');
    await page.getByTestId('estimativa-editar').click();
    await page.locator('app-celula-dialog input').fill('800');
    await page.getByTestId('celula-salvar').click();
    await expect(page.getByTestId('estimativa-texto')).toContainText('Estimado R$ 800,00');
    await expect(page.getByTestId('estimativa-previsto')).toHaveCount(0);

    await page.goto('/mes/2027-05');
    await expect(page.getByTestId('fatura-estimativa').first()).toHaveText('Estimado R$ 800,00');
    await expect(page.getByTestId('resumo-saidas').getByTestId('previsto')).toContainText(
      'R$ 205,53',
    );
  });
});
