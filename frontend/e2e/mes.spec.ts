import { expect, test, type Page } from '@playwright/test';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};
const HOJE = '2026-03-10T12:00:00-03:00';

const azul = {
  id: 'w-azul',
  name: 'Azul',
  type: 'checking',
  color: '#1E88E5',
  initialBalanceCents: 50000,
  initialBalanceDate: '2026-03-01',
  isPrimary: true,
  archived: false,
  currentBalanceCents: 50000,
  hasEntries: true,
  version: 1,
};
const verde = {
  ...azul,
  id: 'w-verde',
  name: 'Verde',
  color: '#43A047',
  isPrimary: false,
  initialBalanceCents: 0,
};

interface Lanc {
  id: string;
  kind: 'income' | 'expense';
  description: string;
  amountCents: number;
  date: string;
  referenceMonth: string;
  status: 'planned' | 'settled';
  walletId: string;
  createdAt: string;
  version: number;
}
const lanc = (
  id: string,
  kind: Lanc['kind'],
  description: string,
  amountCents: number,
  status: Lanc['status'],
  date = '2026-03-12',
): Lanc => ({
  id,
  kind,
  description,
  amountCents,
  date,
  referenceMonth: date.slice(0, 7),
  status,
  walletId: 'w-azul',
  createdAt: '',
  version: 1,
});

/** Exemplo numérico da US3: carteira Azul com 500,00 em 01/03/2026. */
function exemplo(): Lanc[] {
  return [
    lanc('1', 'income', 'Salário', 150000, 'settled', '2026-03-05'),
    lanc('2', 'income', 'Duda', 1827, 'planned', '2026-03-02'),
    lanc('3', 'expense', 'Tim', 4499, 'settled', '2026-03-10'),
    lanc('4', 'expense', 'Léo Casa', 77003, 'planned', '2026-03-20'),
  ];
}

function coluna(lista: Lanc[], abertura: number, soRealizado: boolean) {
  const f = lista.filter((e) => !soRealizado || e.status === 'settled');
  const income = f.filter((e) => e.kind === 'income').reduce((a, e) => a + e.amountCents, 0);
  const expense = f.filter((e) => e.kind === 'expense').reduce((a, e) => a + e.amountCents, 0);
  return {
    openingBalanceCents: abertura,
    walletsInitialBalanceCents: abertura === 50000 && lista.length > 0 ? 50000 : 0,
    incomeCents: income,
    expenseCents: expense,
    resultCents: income - expense,
    transfersInCents: 0,
    transfersOutCents: 0,
    finalBalanceCents: abertura + income - expense,
  };
}

async function simular(page: Page, lista: Lanc[] = exemplo()) {
  const pedidos: string[] = [];
  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (r) => r.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (r) => r.fulfill({ json: [azul, verde] }));
  await page.route('**/api/v1/entries/*/status', (r) => {
    const id = r.request().url().split('/').slice(-2)[0];
    const e = lista.find((x) => x.id === id)!;
    e.status = r.request().postDataJSON().status;
    return r.fulfill({ json: e });
  });
  await page.route('**/api/v1/entries/*', (r) => {
    if (r.request().method() !== 'PUT') return r.fallback();
    const id = r.request().url().split('/').pop()!;
    const e = lista.find((x) => x.id === id)!;
    const corpo = r.request().postDataJSON();
    pedidos.push(`PUT ${corpo.description} v${corpo.version}`);
    e.description = corpo.description;
    e.version++;
    return r.fulfill({ json: e });
  });
  await page.route('**/api/v1/months/*', (r) => {
    const url = new URL(r.request().url());
    const mes = url.pathname.split('/').pop()!;
    pedidos.push(`GET ${mes}${url.search}`);
    const doMes = lista.filter((e) => e.referenceMonth === mes);
    const marco = lista.filter((e) => e.referenceMonth === '2026-03');
    const abertura =
      mes === '2026-03'
        ? 50000
        : mes > '2026-03'
          ? coluna(marco, 50000, false).finalBalanceCents
          : 0;
    const aberturaReal =
      mes === '2026-03'
        ? 50000
        : mes > '2026-03'
          ? coluna(marco, 50000, true).finalBalanceCents
          : 0;
    const planned = coluna(doMes, abertura, false);
    const actual = coluna(doMes, aberturaReal, true);
    if (mes !== '2026-03') {
      planned.walletsInitialBalanceCents = 0;
      actual.walletsInitialBalanceCents = 0;
    }
    return r.fulfill({
      json: {
        month: mes,
        today: '2026-03-10',
        walletIds: null,
        planned,
        actual,
        entries: doMes,
        transfers: [],
        pending: { count: 0, toReceiveCents: 0, toPayCents: 0 },
      },
    });
  });
  return pedidos;
}

const celula = (page: Page, linha: string, col: 'previsto' | 'realizado') =>
  page.getByTestId(`resumo-${linha}`).getByTestId(col);

test.describe('visão do mês (US3) — API simulada', () => {
  test('US3.1: março/2026 mostra exatamente o resumo do exemplo (SC-002)', async ({ page }) => {
    await simular(page);
    await page.goto('/mes/2026-03');

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Março de 2026');
    const esperado: Record<string, [string, string]> = {
      'saldo-inicial': ['R$ 500,00', 'R$ 500,00'],
      entradas: ['R$ 1.518,27', 'R$ 1.500,00'],
      saidas: ['R$ 815,02', 'R$ 44,99'],
      resultado: ['R$ 703,25', 'R$ 1.455,01'],
      'saldo-final': ['R$ 1.203,25', 'R$ 1.955,01'],
    };
    for (const [linha, [p, r]] of Object.entries(esperado)) {
      await expect(celula(page, linha, 'previsto')).toHaveText(p);
      await expect(celula(page, linha, 'realizado')).toHaveText(r);
    }
    await expect(page.getByTestId('resumo-saldo-inicial')).toContainText(
      'inclui saldo inicial de carteiras',
    );
    await expect(page.getByTestId('lancamento')).toHaveCount(4);
  });

  test('US3.2: um toque em "Duda" recalcula o Realizado sem recarregar a tela', async ({
    page,
  }) => {
    await simular(page);
    await page.goto('/mes/2026-03');
    await page.getByRole('checkbox', { name: 'Duda: Recebida' }).click();

    await expect(celula(page, 'entradas', 'realizado')).toHaveText('R$ 1.518,27');
    await expect(celula(page, 'resultado', 'realizado')).toHaveText('R$ 1.473,28');
    await expect(celula(page, 'saldo-final', 'realizado')).toHaveText('R$ 1.973,28');
    await expect(page.getByRole('checkbox', { name: 'Duda: Recebida' })).toBeChecked();
  });

  test('US3.3: seta e deslizar vão para abril/fevereiro mantendo os filtros; abril herda o saldo', async ({
    page,
  }) => {
    await simular(page);
    await page.goto('/mes/2026-03?situacao=planned');
    await page.getByRole('button', { name: 'Próximo mês' }).click();

    await expect(page).toHaveURL(/\/mes\/2026-04\?situacao=planned$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Abril de 2026');
    await expect(celula(page, 'saldo-inicial', 'previsto')).toHaveText('R$ 1.203,25');
    await expect(celula(page, 'saldo-inicial', 'realizado')).toHaveText('R$ 1.955,01');
    await expect(page.getByTestId('mes-vazio')).toHaveText(
      'Nada lançado neste mês. Toque em + para lançar.',
    );

    // deslizar para a direita = mês anterior
    await page.locator('.pagina').evaluate((el) => {
      const evento = (tipo: string, x: number) =>
        el.dispatchEvent(
          new PointerEvent(tipo, { pointerType: 'touch', clientX: x, clientY: 300, bubbles: true }),
        );
      evento('pointerdown', 100);
      evento('pointerup', 260);
    });
    await expect(page).toHaveURL(/\/mes\/2026-03\?situacao=planned$/);
  });

  test('US3.4: resultado previsto negativo aparece em vermelho', async ({ page }) => {
    await simular(page, [lanc('9', 'expense', 'Léo Casa', 90000, 'planned')]);
    await page.goto('/mes/2026-03');
    await expect(celula(page, 'resultado', 'previsto')).toHaveText('-R$ 900,00');
    // vermelho = --saida do tema claro
    await expect(celula(page, 'resultado', 'previsto')).toHaveCSS('color', 'oklch(0.53 0.19 25)');
    await expect(celula(page, 'resultado', 'realizado')).not.toHaveClass(/negativo/);
  });

  test('US3.5: "/" abre o mês de hoje', async ({ page }) => {
    await simular(page);
    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/2026-03$/);
  });

  test('selo "Atrasada" e filtro de situação', async ({ page }) => {
    await simular(page);
    await page.goto('/mes/2026-03');
    await expect(page.getByTestId('atrasada')).toHaveCount(1);
    await page.getByRole('radio', { name: 'Realizadas' }).click();
    await expect(page).toHaveURL(/situacao=settled/);
    await expect(page.getByTestId('lancamento')).toHaveCount(2);
  });

  test('filtro de carteira vai à API e fica na query string', async ({ page }) => {
    const pedidos = await simular(page);
    await page.goto('/mes/2026-03');
    await page.getByTestId('filtro-carteira').filter({ hasText: 'Azul' }).click();
    await expect(page).toHaveURL(/carteiras=w-azul/);
    await expect.poll(() => pedidos.some((p) => p.includes('walletIds=w-azul'))).toBe(true);
  });

  test('tocar no item abre o formulário em edição e salva por PUT', async ({ page }) => {
    const pedidos = await simular(page);
    await page.goto('/mes/2026-03');
    await page.getByRole('button', { name: /^Tim/ }).click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeVisible();
    await page.getByRole('textbox', { name: 'Descrição' }).fill('Tim Pós');
    await page.getByTestId('salvar').click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(pedidos).toContain('PUT Tim Pós v1');
    await expect(page.getByText('Tim Pós')).toBeVisible();
  });

  test('computador: tabela ordenável por coluna (FR-026); celular: lista', async ({
    page,
  }, info) => {
    await simular(page);
    await page.goto('/mes/2026-03');
    if (info.project.name === 'mobile') {
      await expect(page.getByRole('table', { name: /Entradas|Saídas/ })).toHaveCount(0);
      return;
    }
    const tabela = page.getByTestId('tabela-entradas');
    await expect(tabela.getByRole('columnheader')).toHaveText([
      'Descrição',
      'Data',
      'Valor',
      'Carteira',
      'Situação',
    ]);
    const nomes = tabela.getByTestId('lancamento').locator('.abrir');
    await tabela.getByRole('button', { name: 'Valor' }).click();
    await expect(nomes).toHaveText(['Duda', 'Salário']);
    await tabela.getByRole('button', { name: 'Valor' }).click();
    await expect(nomes).toHaveText(['Salário', 'Duda']);
    await expect(tabela.getByRole('columnheader', { name: 'Valor' })).toHaveAttribute(
      'aria-sort',
      'descending',
    );
  });
});

// Contra a stack real (E2E_BASE_URL): o servidor calcula o resumo do exemplo da US3.
test.describe('visão do mês (US3) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  test('exemplo da US3: resumo de março, um toque em "Duda" e abril herdando o saldo', async ({
    page,
  }) => {
    const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
    // O cadastro grava o cookie de sessão no contexto: a página já nasce logada.
    const cadastro = await page.request.post('/api/v1/auth/signup', {
      data: { name: 'Lia Souza', email, password: 'senha-teste-123' },
    });
    expect(cadastro.ok()).toBe(true);
    const { accessToken } = await cadastro.json();
    const auth = { Authorization: `Bearer ${accessToken}` };

    const carteira = await page.request.post('/api/v1/wallets', {
      headers: auth,
      data: {
        name: 'Azul',
        type: 'checking',
        color: '#1E88E5',
        initialBalance: '500,00',
        initialBalanceDate: '2026-03-01',
      },
    });
    expect(carteira.ok()).toBe(true);
    const walletId = (await carteira.json()).id;
    const novo = async (
      kind: string,
      description: string,
      amount: string,
      date: string,
      status: string,
    ) =>
      expect(
        (
          await page.request.post('/api/v1/entries', {
            headers: { ...auth, 'Idempotency-Key': crypto.randomUUID() },
            data: { kind, description, amount, date, walletId, status },
          })
        ).ok(),
      ).toBe(true);
    await novo('income', 'Salário', '1500', '2026-03-05', 'settled');
    await novo('income', 'Duda', '18,27', '2026-03-02', 'planned');
    await novo('expense', 'Tim', '44,99', '2026-03-10', 'settled');
    await novo('expense', 'Léo Casa', '770,03', '2026-03-20', 'planned');

    await page.goto('/mes/2026-03');
    await expect(celula(page, 'saldo-inicial', 'previsto')).toHaveText('R$ 500,00');
    await expect(celula(page, 'entradas', 'previsto')).toHaveText('R$ 1.518,27');
    await expect(celula(page, 'saidas', 'previsto')).toHaveText('R$ 815,02');
    await expect(celula(page, 'resultado', 'previsto')).toHaveText('R$ 703,25');
    await expect(celula(page, 'saldo-final', 'previsto')).toHaveText('R$ 1.203,25');
    await expect(celula(page, 'resultado', 'realizado')).toHaveText('R$ 1.455,01');
    await expect(celula(page, 'saldo-final', 'realizado')).toHaveText('R$ 1.955,01');

    await page.getByRole('checkbox', { name: 'Duda: Recebida' }).click();
    await expect(celula(page, 'entradas', 'realizado')).toHaveText('R$ 1.518,27');
    await expect(celula(page, 'resultado', 'realizado')).toHaveText('R$ 1.473,28');
    await expect(celula(page, 'saldo-final', 'realizado')).toHaveText('R$ 1.973,28');

    await page.getByRole('button', { name: 'Próximo mês' }).click();
    await expect(celula(page, 'saldo-inicial', 'previsto')).toHaveText('R$ 1.203,25');
    await expect(celula(page, 'saldo-inicial', 'realizado')).toHaveText('R$ 1.973,28');
    await expect(page.getByTestId('mes-vazio')).toBeVisible();
  });
});
