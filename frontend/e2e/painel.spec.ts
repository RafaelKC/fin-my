import { expect, test, type Page } from '@playwright/test';
import { irPara } from './navegacao';
import { criarEspacoReal, sessao } from './tags-api';

// 006 T005 — Painel do ano (US1.1–US1.6), resumo "Agora" (US2) e tela inicial do aparelho (FR-006).
// API simulada: números de exemplo, hoje = 20/03/2026. Stack real: o painel confere com a visão do
// mês devolvida pelo servidor (SC-001).

const HOJE = '2026-03-20T12:00:00-03:00';

const coluna = (income: number, expense: number, final: number) => ({
  openingBalanceCents: 0,
  walletsInitialBalanceCents: 0,
  incomeCents: income,
  expenseCents: expense,
  resultCents: income - expense,
  transfersInCents: 0,
  transfersOutCents: 0,
  finalBalanceCents: final,
});

function painel2026() {
  const months = Array.from({ length: 12 }, (_, i) => {
    const month = `2026-${String(i + 1).padStart(2, '0')}`;
    if (i === 0)
      return {
        month,
        planned: coluna(500000, 200000, 300000),
        actual: coluna(450000, 200000, 250000),
      };
    if (i === 1)
      return { month, planned: coluna(100000, 250000, 150000), actual: coluna(0, 0, 250000) };
    return { month, planned: coluna(0, 0, 150000), actual: coluna(0, 0, 250000) };
  });
  return {
    year: 2026,
    today: '2026-03-20',
    months,
    totals: {
      planned: {
        incomeCents: 600000,
        expenseCents: 450000,
        resultCents: 150000,
        finalBalanceCents: 150000,
      },
      actual: {
        incomeCents: 450000,
        expenseCents: 200000,
        resultCents: 250000,
        finalBalanceCents: 250000,
      },
    },
  };
}

const agora = {
  today: '2026-03-20',
  wallets: [
    { id: 'w-azul', name: 'Azul', color: '#1E88E5', currentBalanceCents: 180000 },
    { id: 'w-verde', name: 'Verde', color: '#43A047', currentBalanceCents: 15000 },
  ],
  archivedWalletsBalanceCents: 0,
  totalBalanceCents: 195000,
  invoices: [
    {
      creditCardId: 'c-azul',
      creditCardName: 'Azul',
      dueMonth: '2026-03',
      dueDate: '2026-03-25',
      status: 'closed',
      remainingCents: 20000,
    },
    {
      creditCardId: 'c-verde',
      creditCardName: 'Verde',
      dueMonth: '2026-04',
      dueDate: '2026-04-10',
      status: 'open',
      remainingCents: 30000,
    },
  ],
  pending: { count: 2, toReceiveCents: 10000, toPayCents: 4550 },
  settlements: [
    { tagId: 't-bruna', tagName: 'Bruna', accumulatedCents: 17000 },
    { tagId: 't-carla', tagName: 'Carla', accumulatedCents: -2000 },
  ],
};

async function simular(page: Page): Promise<void> {
  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/now', (route) => route.fulfill({ json: agora }));
  await page.route('**/api/v1/years/2026/panel', (route) => route.fulfill({ json: painel2026() }));
  await page.route('**/api/v1/me/connected-apps', (route) => route.fulfill({ json: [] }));
}

const linha = (page: Page, mes: string) =>
  page.locator(`[data-testid="painel-mes"][data-mes="${mes}"]`);

test.describe('painel (US1, US2, FR-006) — API simulada', () => {
  test.beforeEach(async ({ page }) => simular(page));

  test('US1.1: 12 linhas com os valores do Previsto e a linha de total', async ({ page }) => {
    await page.goto('/painel/2026');
    await expect(page.getByTestId('painel-titulo')).toHaveText('2026');
    await expect(page.getByTestId('painel-mes')).toHaveCount(12);
    const jan = linha(page, '2026-01');
    await expect(jan.locator('th')).toHaveText('JAN');
    await expect(jan.getByTestId('entradas')).toContainText('5.000,00');
    await expect(jan.getByTestId('saidas')).toContainText('2.000,00');
    await expect(jan.getByTestId('resultado')).toContainText('3.000,00');
    await expect(jan.getByTestId('saldo')).toContainText('3.000,00');
    const total = page.getByTestId('painel-total');
    await expect(total.getByTestId('entradas')).toContainText('6.000,00');
    await expect(total.getByTestId('saidas')).toContainText('4.500,00');
    await expect(total.getByTestId('resultado')).toContainText('1.500,00');
    await expect(total.getByTestId('saldo')).toContainText('1.500,00');
  });

  test('US1.2: resultado negativo aparece em vermelho', async ({ page }) => {
    await page.goto('/painel/2026');
    const cor = (mes: string) =>
      linha(page, mes)
        .getByTestId('resultado')
        .evaluate((el) => getComputedStyle(el).color);
    const normal = await cor('2026-01');
    const negativo = await cor('2026-02');
    expect(negativo).not.toBe(normal);
    await expect(linha(page, '2026-02').getByTestId('resultado')).toHaveClass(/negativo/);
    await expect(linha(page, '2026-01').getByTestId('resultado')).not.toHaveClass(/negativo/);
  });

  test('US1.3: tocar num mês abre a visão daquele mês', async ({ page }) => {
    await page.goto('/painel/2026');
    await linha(page, '2026-02').getByTestId('painel-mes-link').click();
    await expect(page).toHaveURL(/\/mes\/2026-02$/);
  });

  test('US1.4: Realizado troca todos os valores', async ({ page }) => {
    await page.goto('/painel/2026');
    await page.getByTestId('leitura-realizado').click();
    await expect(linha(page, '2026-01').getByTestId('entradas')).toContainText('4.500,00');
    await expect(page.getByTestId('painel-total').getByTestId('resultado')).toContainText(
      '2.500,00',
    );
    await expect(page.getByTestId('leitura-realizado')).toHaveAttribute('aria-pressed', 'true');
    await page.getByTestId('leitura-previsto').click();
    await expect(linha(page, '2026-01').getByTestId('entradas')).toContainText('5.000,00');
  });

  test('US1.5: o gráfico mostra os 12 meses e acompanha a leitura; no computador fica ao lado', async ({
    page,
  }) => {
    await page.goto('/painel/2026');
    const grafico = page.getByTestId('grafico');
    await expect(grafico).toBeVisible();
    await expect(page.getByTestId('grafico-mes')).toHaveCount(12);
    await expect(grafico).toHaveAttribute('aria-label', 'Previsto de 2026');
    await page.getByTestId('leitura-realizado').click();
    await expect(grafico).toHaveAttribute('aria-label', 'Realizado de 2026');

    const tabela = (await page.getByTestId('painel-tabela').boundingBox())!;
    const desenho = (await grafico.boundingBox())!;
    if ((page.viewportSize()?.width ?? 0) >= 1024) {
      expect(desenho.x).toBeGreaterThan(tabela.x + tabela.width - 1);
    } else {
      expect(desenho.y).toBeGreaterThan(tabela.y + tabela.height - 1); // celular: abaixo da tabela
    }
  });

  test('US1.6: só a linha do mês de hoje (março) fica destacada', async ({ page }) => {
    await page.goto('/painel/2026');
    await expect(page.locator('[data-testid="painel-mes"].hoje')).toHaveCount(1);
    await expect(linha(page, '2026-03')).toHaveClass(/hoje/);
    await expect(linha(page, '2026-03')).toHaveAttribute('aria-current', 'date');
  });

  test('US2.1: saldo de cada carteira e o total', async ({ page }) => {
    await page.goto('/painel/2026');
    const carteiras = page.getByTestId('agora-carteira');
    await expect(carteiras).toHaveCount(2);
    await expect(carteiras.nth(0)).toContainText('Azul');
    await expect(carteiras.nth(0)).toContainText('1.800,00');
    await expect(carteiras.nth(1)).toContainText('150,00');
    await expect(page.getByTestId('agora-total')).toContainText('1.950,00');
  });

  test('US2.2: faturas próximas com vencimento, situação e Restante', async ({ page }) => {
    await page.goto('/painel/2026');
    const faturas = page.getByTestId('agora-fatura');
    await expect(faturas).toHaveCount(2);
    await expect(faturas.nth(0)).toContainText('Azul');
    await expect(faturas.nth(0)).toContainText('25/03/2026');
    await expect(faturas.nth(0)).toContainText('Fechada');
    await expect(faturas.nth(0)).toContainText('200,00');
    await expect(faturas.nth(1)).toContainText('10/04/2026');
    await expect(faturas.nth(1)).toContainText('300,00');
  });

  test('US2.3: pendências com quantidade e totais, e acesso ao mês', async ({ page }) => {
    await page.goto('/painel/2026');
    await expect(page.getByTestId('agora-pendencias-qtd')).toHaveText('2');
    await expect(page.getByTestId('agora-pendencias-receber')).toContainText('100,00');
    await expect(page.getByTestId('agora-pendencias-pagar')).toContainText('45,50');
    await page.getByTestId('agora-ver-pendencias').click();
    await expect(page).toHaveURL(/\/mes\/2026-03$/);
  });

  test('US2.4: acertos "te deve" e "Crédito da"', async ({ page }) => {
    await page.goto('/painel/2026');
    const acertos = page.getByTestId('agora-acerto');
    await expect(acertos.nth(0)).toContainText(/Bruna te deve R\$\s170,00/);
    await expect(acertos.nth(1)).toContainText(/Crédito da Carla: R\$\s20,00/);
  });

  test('"Painel" no menu leva ao ano de hoje; as setas trocam de ano', async ({ page }) => {
    await page.route('**/api/v1/years/2027/panel', (route) =>
      route.fulfill({ json: { ...painel2026(), year: 2027 } }),
    );
    await page.goto('/carteiras');
    await expect(page.getByRole('heading', { name: 'Carteiras', level: 1 })).toBeVisible();
    await irPara(page, 'Painel');
    await expect(page).toHaveURL(/\/painel\/2026$/);
    await page.getByTestId('painel-seguinte').click();
    await expect(page).toHaveURL(/\/painel\/2027$/);
    await expect(page.getByTestId('painel-titulo')).toHaveText('2027');
  });

  test('FR-006: com o Painel como tela inicial, "/" abre o painel; por padrão abre o mês', async ({
    page,
  }) => {
    await page.route('**/api/v1/months/**', (route) => route.abort());
    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/2026-03$/);

    await page.goto('/perfil');
    await page.getByTestId('tela-inicial-painel').check();
    await page.goto('/');
    await expect(page).toHaveURL(/\/painel\/2026$/);
    await expect(page.getByTestId('painel-tabela')).toBeVisible();

    await page.goto('/perfil');
    await page.getByTestId('tela-inicial-mes').check();
    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/2026-03$/);
  });
});

test.describe('painel (SC-001) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa do compose no ar e de E2E_BASE_URL');

  test('cada linha do painel é idêntica à visão do mês correspondente', async ({ page }) => {
    const api = await criarEspacoReal(page);
    await api.entrada('income', 'Salário Lia', '3000,00', '2026-03-05');
    await api.entrada('expense', 'Aluguel', '1200,00', '2026-03-10');
    await api.entrada('income', 'Freela Léo', '800,00', '2026-04-02');
    const cartao = await api.cartao('Verde', 18, 25);
    await api.compra(cartao.id, 'Mercado', '150,00', '2026-03-05');

    await page.goto('/painel/2026');
    await expect(page.getByTestId('painel-mes')).toHaveCount(12);
    for (const mes of ['2026-03', '2026-04']) {
      const visao = await (
        await page.request.get(`/api/v1/months/${mes}`, { headers: api.auth })
      ).json();
      for (const leitura of ['planned', 'actual'] as const) {
        await page
          .getByTestId(leitura === 'planned' ? 'leitura-previsto' : 'leitura-realizado')
          .click();
        const l = linha(page, mes);
        const brl = (c: number) => (c / 100).toLocaleString('pt-BR', { minimumFractionDigits: 2 });
        const c = visao[leitura];
        await expect(l.getByTestId('entradas')).toContainText(brl(c.incomeCents));
        await expect(l.getByTestId('saidas')).toContainText(brl(c.expenseCents));
        await expect(l.getByTestId('saldo')).toContainText(brl(Math.abs(c.finalBalanceCents)));
      }
    }
    // Realizado de março: 3.000,00 entrou e 1.200,00 saiu (a compra no cartão cai na fatura de abril)
    await page.getByTestId('leitura-realizado').click();
    await expect(linha(page, '2026-03').getByTestId('entradas')).toContainText('3.000,00');
    await expect(linha(page, '2026-03').getByTestId('saidas')).toContainText('1.200,00');
    await expect(page.getByTestId('agora-total')).toContainText('R$');
  });
});
