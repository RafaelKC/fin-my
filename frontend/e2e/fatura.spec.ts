import { expect, test, type Page } from '@playwright/test';

// 003 T009 — tela `/cartoes/:id/faturas/:mes`: totais e situação, conferida (US7.1), mover (US7.2,
// US7.3), excluir com escopo e Desfazer, pagamentos (US5.2, US5.5), levar restante (US5.4) e
// navegação entre faturas (US6.2). A API simulada guarda o estado em memória; a stack real
// confere as mesmas regras no servidor.

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};

interface Compra {
  id: string;
  mes: string;
  descricao: string;
  centavos: number;
  nature: 'debit' | 'credit';
  reconciled: boolean;
  moved: boolean;
  plano: string | null;
  numero: number | null;
  total: number | null;
  origem: 'manual' | 'installment' | 'carry_over';
}

interface Pagamento {
  id: string;
  mes: string;
  walletId: string;
  centavos: number;
  date: string;
}

const brl = (c: number) => `R$ ${(c / 100).toFixed(2).replace('.', ',')}`;
const mesDe = (mes: string, delta: number) => {
  const [a, m] = mes.split('-').map(Number);
  const d = new Date(Date.UTC(a, m - 1 + delta, 1));
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}`;
};
const problema = (status: number, code: string, detail: string) => ({
  status,
  contentType: 'application/problem+json',
  json: { type: 'about:blank', title: code, status, code, detail },
});

const carteira = (id: string, name: string) => ({
  id,
  name,
  type: 'checking',
  color: '#1E88E5',
  initialBalanceCents: 0,
  initialBalanceDate: '2026-01-01',
  isPrimary: id === 'w-azul',
  archived: false,
  currentBalanceCents: 0,
  hasEntries: false,
  version: 1,
});

/** Cartão Azul (fecha 18, vence 25) com a fatura de março do exemplo da US5; hoje = 26/03/2026. */
async function simular(page: Page, compras: Compra[], pagamentos: Pagamento[] = []) {
  let seq = 100;
  const lotes = new Map<string, { compras: Compra[]; pagamentos: Pagamento[] }>();
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
    availableLimitCents: null,
  };
  const resumo = (mes: string) => {
    const total = compras
      .filter((c) => c.mes === mes)
      .reduce((s, c) => s + (c.nature === 'credit' ? -c.centavos : c.centavos), 0);
    const pago = pagamentos.filter((p) => p.mes === mes).reduce((s, p) => s + p.centavos, 0);
    const restante = total - pago;
    const status = total <= 0 ? 'no_amount_due' : restante <= 0 ? 'paid' : 'overdue';
    return { total, pago, restante, status };
  };
  const dto = (c: Compra) => ({
    id: c.id,
    creditCardId: 'c-azul',
    dueMonth: c.mes,
    description: c.descricao,
    displayDescription: c.total ? `${c.descricao} ${c.numero}/${c.total}` : c.descricao,
    amountCents: c.centavos,
    nature: c.nature,
    date: '2026-03-10',
    origin: c.origem,
    reconciled: c.reconciled,
    manuallyMoved: c.moved,
    notes: null,
    installmentPlanId: c.plano,
    installmentNumber: c.numero,
    installmentTotal: c.total,
    createdAt: '2026-03-10T12:00:00Z',
    version: 1,
  });
  const fatura = (mes: string) => {
    const r = resumo(mes);
    const doMes = compras.filter((c) => c.mes === mes);
    return {
      creditCardId: 'c-azul',
      dueMonth: mes,
      closingDate: `${mes}-18`,
      dueDate: `${mes}-25`,
      id: null,
      status: r.status,
      totalCents: r.total,
      paidCents: r.pago,
      remainingCents: r.restante,
      reconciledCents: doMes
        .filter((c) => c.reconciled)
        .reduce((s, c) => s + (c.nature === 'credit' ? -c.centavos : c.centavos), 0),
      canCarryOver: r.restante !== 0,
      purchases: doMes.map(dto),
      payments: pagamentos
        .filter((p) => p.mes === mes)
        .map((p) => ({
          id: p.id,
          creditCardId: 'c-azul',
          dueMonth: mes,
          walletId: p.walletId,
          amountCents: p.centavos,
          date: p.date,
          version: 1,
        })),
      previousMonth: mesDe(mes, -1),
      nextMonth: mesDe(mes, 1),
    };
  };
  const mesDaCompra = (id: string) => compras.find((c) => c.id === id)!;

  await page.clock.setFixedTime('2026-03-26T12:00:00-03:00');
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) =>
    route.fulfill({ json: [carteira('w-azul', 'Azul'), carteira('w-verde', 'Verde')] }),
  );
  await page.route('**/api/v1/credit-cards/c-azul', (route) =>
    route.fulfill({
      json: {
        ...cartao,
        currentInvoice: fatura('2026-03'),
        nextInvoice: fatura('2026-04'),
        openPastInvoices: [],
      },
    }),
  );
  await page.route('**/api/v1/credit-cards/c-azul/invoices/**', async (route) => {
    const req = route.request();
    const [, , , , , , mes, acao] = new URL(req.url()).pathname.split('/');
    if (!acao) return route.fulfill({ json: fatura(mes) });
    if (acao === 'payments' && req.method() === 'POST') {
      const corpo = req.postDataJSON() ?? {};
      const r = resumo(mes);
      const valor = corpo.amount
        ? Math.round(Number(String(corpo.amount).replace(',', '.')) * 100)
        : r.restante;
      if (valor > r.restante)
        return route.fulfill(
          problema(
            422,
            'invoice.payment_exceeds_remaining',
            `O valor em aberto é ${brl(r.restante)}.`,
          ),
        );
      pagamentos.push({
        id: `pg${++seq}`,
        mes,
        walletId: corpo.walletId ?? 'w-azul',
        centavos: valor,
        date: corpo.date ?? '2026-03-26',
      });
      return route.fulfill({ status: 201, json: fatura(mes) });
    }
    if (acao === 'carry-over') {
      const r = resumo(mes);
      compras.push(
        {
          id: `p${++seq}`,
          mes,
          descricao: 'Saldo levado para 04/2026',
          centavos: r.restante,
          nature: 'credit',
          reconciled: false,
          moved: false,
          plano: null,
          numero: null,
          total: null,
          origem: 'carry_over',
        },
        {
          id: `p${++seq}`,
          mes: mesDe(mes, 1),
          descricao: 'Saldo da fatura 03/2026',
          centavos: r.restante,
          nature: 'debit',
          reconciled: false,
          moved: false,
          plano: null,
          numero: null,
          total: null,
          origem: 'carry_over',
        },
      );
      return route.fulfill({ status: 201, json: fatura(mes) });
    }
    return route.fulfill(problema(404, 'not_found', 'Não encontrada.'));
  });
  await page.route('**/api/v1/card-purchases/**', async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const [, , , , id, acao] = url.pathname.split('/');
    const compra = mesDaCompra(id);
    if (acao === 'reconciled') {
      compra.reconciled = req.postDataJSON().reconciled;
      return route.fulfill({ json: dto(compra) });
    }
    if (acao === 'move') {
      const { direction, scope } = req.postDataJSON();
      const passo = direction === 'next' ? 1 : -1;
      const alvo =
        scope === 'this_and_following' && compra.plano
          ? compras.filter(
              (c) => c.plano === compra.plano && (c.numero ?? 0) >= (compra.numero ?? 0),
            )
          : [compra];
      alvo.forEach((c) => {
        c.mes = mesDe(c.mes, passo);
        c.moved = true;
      });
      return route.fulfill({ json: alvo.map(dto) });
    }
    if (req.method() === 'DELETE') {
      const escopo = url.searchParams.get('scope');
      const alvo = compras.filter(
        (c) =>
          c.id === id ||
          (compra.plano &&
            c.plano === compra.plano &&
            (escopo === 'all' ||
              (escopo === 'this_and_following' && (c.numero ?? 0) >= (compra.numero ?? 0)))),
      );
      const lote = `lote${++seq}`;
      lotes.set(lote, { compras: alvo, pagamentos: [] });
      alvo.forEach((c) => compras.splice(compras.indexOf(c), 1));
      return route.fulfill({ json: { deletionBatchId: lote } });
    }
    return route.fulfill(problema(404, 'not_found', 'Não encontrada.'));
  });
  await page.route('**/api/v1/deletions/*/restore', (route) => {
    const lote = lotes.get(new URL(route.request().url()).pathname.split('/')[4]);
    lote?.compras.forEach((c) => compras.push(c));
    lote?.pagamentos.forEach((p) => pagamentos.push(p));
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/v1/invoice-payments/*', (route) => {
    const req = route.request();
    const id = new URL(req.url()).pathname.split('/')[4];
    const p = pagamentos.find((x) => x.id === id)!;
    if (req.method() === 'PUT') {
      const corpo = req.postDataJSON();
      p.centavos = Math.round(Number(String(corpo.amount).replace(',', '.')) * 100);
      p.date = corpo.date;
      p.walletId = corpo.walletId;
      return route.fulfill({ json: { ...p, amountCents: p.centavos, version: 2 } });
    }
    const lote = `lote${++seq}`;
    lotes.set(lote, { compras: [], pagamentos: [p] });
    pagamentos.splice(pagamentos.indexOf(p), 1);
    return route.fulfill({ json: { deletionBatchId: lote } });
  });
}

const nova = (
  id: string,
  descricao: string,
  centavos: number,
  extra: Partial<Compra> = {},
): Compra => ({
  id,
  mes: '2026-03',
  descricao,
  centavos,
  nature: 'debit',
  reconciled: false,
  moved: false,
  plano: null,
  numero: null,
  total: null,
  origem: 'manual',
  ...extra,
});

/** Exemplo da US5: 12,95 + 33,33 + 44,00 + 209,62 − 99,90 = R$ 200,00. */
const exemplo = () => [
  nova('uber', 'Uber', 1295),
  nova('china', 'Casa China', 3333, { plano: 'plano-china', numero: 2, total: 3 }),
  nova('shopee', 'Shopee', 4400),
  nova('oculos', 'Estorno Óculos', 9990, { nature: 'credit' }),
  nova('lanche', 'Lanche', 20962),
];

const linha = (page: Page, texto: string) => page.getByTestId('compra').filter({ hasText: texto });

test.describe('fatura (US5, US6.2, US7) — API simulada', () => {
  test('mostra datas, situação, totais e a lista; navega entre faturas (US5.2, US6.2)', async ({
    page,
  }) => {
    await simular(page, exemplo());
    await page.goto('/cartoes/c-azul/faturas/2026-03');

    await expect(page.getByTestId('titulo-fatura')).toHaveText('Fatura de março');
    await expect(page.getByTestId('datas-fatura')).toHaveText('Fecha em 18/03 · vence em 25/03');
    await expect(page.getByTestId('situacao')).toHaveText('Vencida');
    await expect(page.getByTestId('total')).toContainText('R$ 200,00');
    await expect(page.getByTestId('pago')).toContainText('R$ 0,00');
    await expect(page.getByTestId('restante')).toContainText('R$ 200,00');
    await expect(page.getByTestId('compra')).toHaveCount(5);
    await expect(linha(page, 'Casa China 2/3')).toBeVisible();

    await page.getByTestId('fatura-seguinte').click();
    await expect(page).toHaveURL(/\/cartoes\/c-azul\/faturas\/2026-04$/);
    await expect(page.getByTestId('titulo-fatura')).toHaveText('Fatura de abril');
    await expect(page.getByTestId('situacao')).toHaveText('Sem valor a pagar');
    await expect(page.getByTestId('sem-compras')).toBeVisible();
    await page.getByTestId('fatura-anterior').click();
    await expect(page.getByTestId('titulo-fatura')).toHaveText('Fatura de março');
  });

  test('conferir compras atualiza "Conferido R$ X de R$ Y" (US7.1)', async ({ page }) => {
    await simular(page, exemplo());
    await page.goto('/cartoes/c-azul/faturas/2026-03');
    await expect(page.getByTestId('conferido')).toHaveText('Conferido R$ 0,00 de R$ 200,00');

    await linha(page, 'Uber').getByTestId('conferida').check();
    await linha(page, 'Lanche').getByTestId('conferida').check();
    await expect(page.getByTestId('conferido')).toHaveText('Conferido R$ 222,57 de R$ 200,00');
    await linha(page, 'Estorno').getByTestId('conferida').check();
    await expect(page.getByTestId('conferido')).toHaveText('Conferido R$ 122,67 de R$ 200,00');
    await linha(page, 'Uber').getByTestId('conferida').uncheck();
    await expect(page.getByTestId('conferido')).toHaveText('Conferido R$ 109,72 de R$ 200,00');
  });

  test('mover para a próxima fatura marca "movida manualmente" (US7.2)', async ({ page }) => {
    await simular(page, exemplo());
    await page.goto('/cartoes/c-azul/faturas/2026-03');

    await linha(page, 'Shopee').getByTestId('mover-seguinte').click();
    await expect(linha(page, 'Shopee')).toHaveCount(0);
    await expect(page.getByTestId('total')).toContainText('R$ 156,00');

    await page.getByTestId('fatura-seguinte').click();
    await expect(linha(page, 'Shopee')).toContainText('movida manualmente');
    await expect(page.getByTestId('total')).toContainText('R$ 44,00');
  });

  test('parcela 2/3: "esta e as seguintes" move e a escolha "só esta" exclui só ela (US7.3)', async ({
    page,
  }) => {
    await simular(page, [
      nova('c2', 'Casa China', 3333, { plano: 'p', numero: 2, total: 3 }),
      nova('c3', 'Casa China', 3333, { mes: '2026-04', plano: 'p', numero: 3, total: 3 }),
    ]);
    await page.goto('/cartoes/c-azul/faturas/2026-03');
    await linha(page, 'Casa China 2/3').getByTestId('mover-seguinte').click();
    const dialogo = page.getByRole('dialog');
    await expect(dialogo).toContainText('Mover só esta parcela ou esta e as seguintes?');
    await dialogo.getByTestId('escopo-this_and_following').click();
    await expect(page.getByTestId('compra')).toHaveCount(0);

    await page.getByTestId('fatura-seguinte').click();
    await expect(page.getByTestId('compra')).toHaveCount(1);
    await page.getByTestId('fatura-seguinte').click();
    await expect(linha(page, 'Casa China 3/3')).toContainText('movida manualmente');
  });

  test('exclui parcela com escopo e desfaz em 8 s (FR-016, RT-09)', async ({ page }) => {
    await simular(page, [
      nova('c1', 'Casa China', 3333, { plano: 'p', numero: 1, total: 3 }),
      nova('c2', 'Casa China', 3333, { mes: '2026-04', plano: 'p', numero: 2, total: 3 }),
      nova('c3', 'Casa China', 3333, { mes: '2026-05', plano: 'p', numero: 3, total: 3 }),
    ]);
    await page.goto('/cartoes/c-azul/faturas/2026-03');
    await linha(page, 'Casa China 1/3').getByTestId('excluir-compra').click();
    const dialogo = page.getByRole('dialog');
    await expect(dialogo.getByTestId('escopo-this')).toHaveText('Só esta');
    await expect(dialogo.getByTestId('escopo-this_and_following')).toBeVisible();
    await dialogo.getByTestId('escopo-all').click();
    await expect(page.getByTestId('compra')).toHaveCount(0);

    await page.getByRole('button', { name: 'Desfazer' }).click();
    await expect(linha(page, 'Casa China 1/3')).toBeVisible();
  });

  test('exclui compra à vista direto, com Desfazer', async ({ page }) => {
    await simular(page, exemplo());
    await page.goto('/cartoes/c-azul/faturas/2026-03');
    await linha(page, 'Uber').getByTestId('excluir-compra').click();
    await expect(linha(page, 'Uber')).toHaveCount(0);
    await expect(page.getByTestId('total')).toContainText('R$ 187,05');
    await page.getByRole('button', { name: 'Desfazer' }).click();
    await expect(page.getByTestId('total')).toContainText('R$ 200,00');
  });

  test('pagamento parcial de 150 → Restante 50; 60 é recusado; levar restante quita (US5.2, US5.4, US5.5)', async ({
    page,
  }) => {
    await simular(page, exemplo());
    await page.goto('/cartoes/c-azul/faturas/2026-03');

    await page.getByTestId('pagar').click();
    const dialogo = page.getByRole('dialog');
    await expect(dialogo.getByLabel('Valor')).toHaveValue('200,00');
    await expect(dialogo.getByLabel('Carteira')).toHaveValue('w-azul');
    await dialogo.getByLabel('Valor').fill('150');
    await dialogo.getByTestId('confirmar-pagamento').click();
    await expect(dialogo).toHaveCount(0);
    await expect(page.getByTestId('pago')).toContainText('R$ 150,00');
    await expect(page.getByTestId('restante')).toContainText('R$ 50,00');
    await expect(page.getByTestId('situacao')).toHaveText('Vencida');
    await expect(page.getByTestId('pagamento')).toHaveCount(1);

    await page.getByTestId('pagar').click();
    await page.getByRole('dialog').getByLabel('Valor').fill('60');
    await page.getByRole('dialog').getByTestId('confirmar-pagamento').click();
    await expect(page.getByTestId('erro-pagamento')).toHaveText('O valor em aberto é R$ 50,00.');
    await page.getByRole('dialog').getByRole('button', { name: 'Cancelar' }).click();

    await page.getByTestId('levar-restante').click();
    await expect(page.getByTestId('situacao')).toHaveText('Paga');
    await expect(linha(page, 'Saldo levado para 04/2026')).toBeVisible();
    await expect(page.getByTestId('total')).toContainText('R$ 150,00');
    await expect(page.getByTestId('levar-restante')).toHaveCount(0);
    await expect(page.getByTestId('pagar')).toHaveCount(0);
    await expect(linha(page, 'Saldo levado para 04/2026').getByTestId('editar-compra')).toHaveCount(
      0,
    );

    await page.getByTestId('fatura-seguinte').click();
    await expect(linha(page, 'Saldo da fatura 03/2026')).toBeVisible();
  });

  test('edita e exclui um pagamento, com Desfazer (FR-018)', async ({ page }) => {
    await simular(page, exemplo(), [
      { id: 'pg1', mes: '2026-03', walletId: 'w-azul', centavos: 15000, date: '2026-03-20' },
    ]);
    await page.goto('/cartoes/c-azul/faturas/2026-03');
    await expect(page.getByTestId('pagamento')).toContainText('20/03');

    await page.getByTestId('editar-pagamento').click();
    const dialogo = page.getByRole('dialog');
    await dialogo.getByLabel('Valor').fill('100');
    await dialogo.getByLabel('Carteira').selectOption({ label: 'Verde' });
    await dialogo.getByTestId('confirmar-pagamento').click();
    await expect(page.getByTestId('pago')).toContainText('R$ 100,00');
    await expect(page.getByTestId('pagamento')).toContainText('Verde');

    await page.getByTestId('excluir-pagamento').click();
    await expect(page.getByTestId('sem-pagamentos')).toBeVisible();
    await expect(page.getByTestId('pago')).toContainText('R$ 0,00');
    await page.getByRole('button', { name: 'Desfazer' }).click();
    await expect(page.getByTestId('pagamento')).toHaveCount(1);
  });

  test('"Editar" abre o formulário com a compra preenchida', async ({ page }) => {
    await simular(page, exemplo());
    await page.route('**/api/v1/credit-cards?**', (route) =>
      route.fulfill({
        json: [
          {
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
            currentInvoice: {},
            nextInvoice: {},
            openPastInvoices: [],
            availableLimitCents: null,
          },
        ],
      }),
    );
    await page.goto('/cartoes/c-azul/faturas/2026-03');
    await linha(page, 'Uber').getByTestId('editar-compra').click();
    const dialogo = page.getByRole('dialog');
    await expect(dialogo.getByRole('heading', { name: 'Editar compra' })).toBeVisible();
    await expect(dialogo.getByLabel('Valor', { exact: true })).toHaveValue('12,95');
    await expect(dialogo.getByRole('textbox', { name: 'Descrição' })).toHaveValue('Uber');
  });

  test('mês inválido na rota mostra erro', async ({ page }) => {
    await simular(page, []);
    await page.goto('/cartoes/c-azul/faturas/abc');
    await expect(page.getByTestId('erro-fatura')).toContainText('Fatura não encontrada');
  });
});

// Contra a stack real (E2E_BASE_URL): o servidor calcula situação, restante e o par do saldo levado.
test.describe('fatura (US5, US7) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  /** Fatura de dois meses atrás: já fechou e venceu (situação Vencida, levar restante liberado). */
  const mesPassado = mesDe(new Date().toISOString().slice(0, 7), -2);

  async function prepararFatura(page: Page) {
    const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
    await page.goto('/criar-conta');
    await page.getByLabel('Nome', { exact: true }).fill('Lia Souza');
    await page.getByLabel('E-mail').fill(email);
    await page.getByLabel('Senha', { exact: true }).fill('senha-teste-123');
    await page.getByRole('button', { name: 'Criar conta' }).click();
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);

    for (const nome of ['Azul', 'Verde']) {
      await page.goto('/carteiras/nova');
      await page.getByLabel('Nome', { exact: true }).fill(nome);
      await page.getByLabel('Saldo inicial', { exact: true }).fill('500,00');
      await page.getByRole('button', { name: 'Salvar' }).click();
      await expect(page).toHaveURL(/\/carteiras$/);
    }
    await page.goto('/cartoes/novo');
    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Dia de fechamento').fill('18');
    await page.getByLabel('Dia de vencimento').fill('25');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page).toHaveURL(/\/cartoes$/);

    await page.goto('/');
    for (const [valor, descricao] of [
      ['12,95', 'Uber'],
      ['209,62', 'Lanche'],
      ['-22,57', 'Estorno Óculos'],
    ]) {
      await page.getByTestId('lancar').click();
      await page.getByLabel('Valor', { exact: true }).fill(valor);
      await page.getByRole('textbox', { name: 'Descrição' }).fill(descricao);
      await page.locator('#lancamento-data').fill(`${mesPassado}-10`);
      await page.getByTestId('salvar').click();
      await expect(page.getByRole('dialog')).toHaveCount(0);
    }
    await page.goto('/cartoes');
    await page
      .getByTestId('cartao')
      .getByRole('link', { name: /Fatura de|Próxima/ })
      .first()
      .click();
    await expect(page).toHaveURL(/\/cartoes\/[^/]+\/faturas\/\d{4}-\d{2}$/);
    const cartaoId = page.url().split('/cartoes/')[1].split('/')[0];
    await page.goto(`/cartoes/${cartaoId}/faturas/${mesPassado}`);
    await expect(page.getByTestId('compra')).toHaveCount(3);
    return cartaoId;
  }

  test('US5.2, US5.4, US5.5, US7.1, US7.2: pagar parcial, recusar excesso, levar restante, conferir e mover', async ({
    page,
  }) => {
    const cartaoId = await prepararFatura(page);

    await expect(page.getByTestId('total')).toContainText('R$ 200,00');
    await expect(page.getByTestId('situacao')).toHaveText('Vencida');

    await linha(page, 'Uber').getByTestId('conferida').check();
    await expect(page.getByTestId('conferido')).toHaveText('Conferido R$ 12,95 de R$ 200,00');

    await page.getByTestId('pagar').click();
    const dialogo = page.getByRole('dialog');
    await dialogo.getByLabel('Valor').fill('150');
    await dialogo.getByTestId('confirmar-pagamento').click();
    await expect(page.getByTestId('pago')).toContainText('R$ 150,00');
    await expect(page.getByTestId('restante')).toContainText('R$ 50,00');
    await expect(page.getByTestId('situacao')).toHaveText('Vencida');

    await page.getByTestId('pagar').click();
    await page.getByRole('dialog').getByLabel('Valor').fill('60');
    await page.getByRole('dialog').getByTestId('confirmar-pagamento').click();
    await expect(page.getByTestId('erro-pagamento')).toContainText('O valor em aberto é R$ 50,00');
    await page.getByRole('dialog').getByRole('button', { name: 'Cancelar' }).click();

    await page.getByTestId('levar-restante').click();
    await expect(page.getByTestId('situacao')).toHaveText('Paga');
    await expect(page.getByTestId('total')).toContainText('R$ 150,00');
    await expect(page.getByTestId('compra')).toHaveCount(4);

    await page.getByTestId('fatura-seguinte').click();
    await expect(page.getByTestId('compra')).toHaveCount(1);
    await expect(page.getByTestId('total')).toContainText('R$ 50,00');
    await page.goto(`/cartoes/${cartaoId}/faturas/${mesPassado}`);

    // US7.2: mover a Uber (já na fatura paga) para a seguinte e conferir a marca.
    await linha(page, 'Uber').getByTestId('mover-seguinte').click();
    await expect(linha(page, 'Uber')).toHaveCount(0);
    await page.getByTestId('fatura-seguinte').click();
    await expect(linha(page, 'Uber')).toContainText('movida manualmente');

    // Excluir com Desfazer.
    await linha(page, 'Uber').getByTestId('excluir-compra').click();
    await expect(linha(page, 'Uber')).toHaveCount(0);
    await page.getByRole('button', { name: 'Desfazer' }).click();
    await expect(linha(page, 'Uber')).toBeVisible();
  });

  test('edita e exclui um pagamento (FR-018) e abre a compra para editar', async ({ page }) => {
    await prepararFatura(page);
    await page.getByTestId('pagar').click();
    await page.getByRole('dialog').getByTestId('confirmar-pagamento').click();
    await expect(page.getByTestId('situacao')).toHaveText('Paga');

    await page.getByTestId('editar-pagamento').click();
    await page.getByRole('dialog').getByLabel('Valor').fill('100');
    await page.getByRole('dialog').getByTestId('confirmar-pagamento').click();
    await expect(page.getByTestId('pago')).toContainText('R$ 100,00');
    await expect(page.getByTestId('situacao')).toHaveText('Vencida');

    await page.getByTestId('excluir-pagamento').click();
    await expect(page.getByTestId('sem-pagamentos')).toBeVisible();
    await page.getByRole('button', { name: 'Desfazer' }).click();
    await expect(page.getByTestId('pagamento')).toHaveCount(1);

    await linha(page, 'Lanche').getByTestId('editar-compra').click();
    const dialogo = page.getByRole('dialog');
    await dialogo.getByRole('textbox', { name: 'Descrição' }).fill('Lanche da Bruna');
    await dialogo.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(linha(page, 'Lanche da Bruna')).toBeVisible();
  });
});
