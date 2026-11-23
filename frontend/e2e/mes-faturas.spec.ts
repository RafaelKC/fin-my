import { expect, test, type Page } from '@playwright/test';

// 003 T011 — linhas "Fatura <Cartão>" na visão do mês (US5.1, US5.6; FR-019, FR-021) e o escopo de
// parcela no formulário rápido (US3.4, US3.5). A API simulada guarda o estado em memória; a stack
// real confere as mesmas regras no servidor. Exemplo da US5: hoje 20/03/2026, fatura Azul = R$ 200,00.

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
  initialBalanceDate: '2026-01-01',
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

interface Estado {
  pago: number;
  /** Corpos recebidos, para conferir o escopo enviado. */
  puts: Record<string, unknown>[];
  deletes: string[];
  parcelas: number[];
}

const TOTAL = 20000;

async function simular(page: Page): Promise<Estado> {
  const estado: Estado = { pago: 0, puts: [], deletes: [], parcelas: [1, 2, 3] };
  let lote = 0;
  const fatura = () => {
    const restante = TOTAL - estado.pago;
    return {
      creditCardId: 'c-azul',
      dueMonth: '2026-03',
      closingDate: '2026-03-18',
      dueDate: '2026-03-25',
      id: 'f1',
      status: restante <= 0 ? 'paid' : 'closed',
      totalCents: TOTAL,
      paidCents: estado.pago,
      remainingCents: restante,
    };
  };
  const compra = (n: number) => ({
    id: `parcela-${n}`,
    creditCardId: 'c-azul',
    dueMonth: '2026-03',
    description: 'Casa China',
    displayDescription: `Casa China ${n}/3`,
    amountCents: 3333,
    nature: 'debit',
    date: '2026-01-28',
    origin: 'installment',
    reconciled: false,
    manuallyMoved: false,
    notes: null,
    installmentPlanId: 'plano-china',
    installmentNumber: n,
    installmentTotal: 3,
    createdAt: '2026-01-28T12:00:00Z',
    version: 1,
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
    nextInvoice: {
      ...fatura(),
      dueMonth: '2026-04',
      totalCents: 0,
      paidCents: 0,
      remainingCents: 0,
    },
    openPastInvoices: [],
    availableLimitCents: null,
  };

  await page.clock.setFixedTime('2026-03-20T12:00:00-03:00');
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteira] }));
  await page.route('**/api/v1/entries/suggestions**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/months/2026-03**', (route) => {
    const f = fatura();
    return route.fulfill({
      json: {
        month: '2026-03',
        today: '2026-03-20',
        walletIds: null,
        planned: coluna(Math.max(TOTAL, estado.pago)),
        actual: coluna(estado.pago),
        entries: [],
        transfers: [],
        invoices: [
          {
            ...f,
            creditCardName: 'Azul',
            plannedCents: Math.max(TOTAL, estado.pago),
            actualCents: estado.pago,
          },
        ],
        pending: { count: 0, toReceiveCents: 0, toPayCents: 0 },
      },
    });
  });
  await page.route('**/api/v1/credit-cards**', async (route) => {
    const req = route.request();
    const partes = new URL(req.url()).pathname.split('/'); // '', api, v1, credit-cards, id, invoices, mes, acao
    const [, , , , id, , , acao] = partes;
    if (!id) return route.fulfill({ json: [cartao] });
    if (!acao && partes.length === 5) return route.fulfill({ json: cartao });
    if (acao === 'payments') {
      if (req.method() === 'POST') {
        estado.pago = TOTAL;
        return route.fulfill({ status: 201, json: fatura() });
      }
      estado.pago = 0;
      return route.fulfill({ json: { deletionBatchId: `lote-${++lote}` } });
    }
    return route.fulfill({
      json: {
        ...fatura(),
        reconciledCents: 0,
        canCarryOver: false,
        purchases: estado.parcelas.map(compra),
        payments: [],
        previousMonth: '2026-02',
        nextMonth: '2026-04',
      },
    });
  });
  await page.route('**/api/v1/card-purchases/*', (route) => {
    const req = route.request();
    const id = new URL(req.url()).pathname.split('/')[4];
    if (req.method() === 'PUT') {
      estado.puts.push(req.postDataJSON());
      return route.fulfill({ json: compra(Number(id.split('-')[1])) });
    }
    if (req.method() === 'DELETE') {
      estado.deletes.push(new URL(req.url()).searchParams.get('scope') ?? '');
      return route.fulfill({ json: { deletionBatchId: `lote-${++lote}` } });
    }
    return route.fulfill({ json: compra(Number(id.split('-')[1])) });
  });
  await page.route('**/api/v1/deletions/*/restore', (route) => route.fulfill({ status: 204 }));
  return estado;
}

test.describe('visão do mês com faturas (US5.1, US5.6) — API simulada', () => {
  test('mostra a fatura nas Saídas; a caixinha paga e desmarcar desfaz os pagamentos', async ({
    page,
  }) => {
    await simular(page);
    await page.goto('/mes/2026-03');

    const linha = page.getByTestId('fatura-mes');
    await expect(linha).toContainText('Fatura Azul');
    await expect(linha.getByTestId('fatura-situacao')).toHaveText('Fechada');
    await expect(linha).toContainText('R$ 200,00');

    // US5.1: marcar paga o restante (R$ 200,00) e a fatura fica Paga; Saídas Realizado sobe.
    await linha.getByTestId('fatura-caixinha').check();
    await expect(linha.getByTestId('fatura-situacao')).toHaveText('Paga');
    await expect(page.getByTestId('resumo-saidas').getByTestId('realizado')).toContainText(
      'R$ 200,00',
    );

    // US5.6: desmarcar exclui os pagamentos, volta a Fechada e oferece Desfazer.
    await linha.getByTestId('fatura-caixinha').uncheck();
    await expect(linha.getByTestId('fatura-situacao')).toHaveText('Fechada');
    await expect(page.getByText('Excluído')).toBeVisible();
    await expect(page.getByTestId('resumo-saidas').getByTestId('realizado')).toContainText(
      'R$ 0,00',
    );
  });

  test('tocar na linha abre a fatura; editar parcela pergunta o escopo (US3.4)', async ({
    page,
  }) => {
    const estado = await simular(page);
    await page.goto('/mes/2026-03');
    await page.getByTestId('fatura-abrir').click();
    await expect(page).toHaveURL(/\/cartoes\/c-azul\/faturas\/2026-03$/);
    await expect(page.getByTestId('titulo-fatura')).toHaveText('Fatura de março');

    await page.getByTestId('editar-compra').first().click();
    await page.locator('#lancamento-descricao').fill('Casa China Nova');
    await page.getByTestId('salvar').click();

    await expect(page.getByText('Alterar só esta parcela ou todas?')).toBeVisible();
    await page.getByTestId('escopo-all').click();
    await expect.poll(() => estado.puts.length).toBe(1);
    expect(estado.puts[0]).toMatchObject({ description: 'Casa China Nova', scope: 'all' });
  });

  test('excluir parcela no formulário oferece os três escopos e o Desfazer (US3.5)', async ({
    page,
  }) => {
    const estado = await simular(page);
    await page.goto('/cartoes/c-azul/faturas/2026-03');
    await page.getByTestId('editar-compra').first().click();
    await page.getByTestId('excluir').click();

    await expect(page.getByTestId('escopo-this')).toBeVisible();
    await expect(page.getByTestId('escopo-this_and_following')).toBeVisible();
    await expect(page.getByTestId('escopo-all')).toBeVisible();
    await page.getByTestId('escopo-this_and_following').click();

    await expect.poll(() => estado.deletes).toEqual(['this_and_following']);
    await expect(page.getByText('Excluído')).toBeVisible();
    await expect(page.getByText('Desfazer')).toBeVisible();
  });
});
