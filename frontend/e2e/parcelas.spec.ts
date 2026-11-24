import { expect, test, type Page } from '@playwright/test';
import { dispensarGuiaSeAberto } from './espaco-novo';
import { simularCompras } from './compras-api';

// 003 T007 — compra parcelada nos dois modos (US3.1–US3.3) e parcelamento em andamento (US4).

const abrir = async (page: Page) => {
  await page.goto('/');
  await page.getByTestId('lancar').click();
  await expect(page.getByRole('radiogroup', { name: 'Tipo de lançamento' })).toBeVisible();
};
const valor = (page: Page) => page.getByLabel('Valor', { exact: true });
const descricao = (page: Page) => page.getByRole('textbox', { name: 'Descrição' });
const cartao = (page: Page) => page.getByRole('combobox', { name: 'Cartão' });
const parcelas = (page: Page) => page.locator('#lancamento-parcelas');
const data = (page: Page) => page.locator('#lancamento-data');
const previa = (page: Page) => page.getByTestId('previa-fatura');
const salvar = (page: Page) => page.getByTestId('salvar');
const modo = (page: Page, nome: string) => page.getByRole('radio', { name: nome, exact: true });
const andamento = (page: Page) => page.getByTestId('em-andamento');

test.describe('aba Cartão — parcelas (US3, US4) — API simulada', () => {
  test('US3.3 e US3.1: 100,00 em 3x mostra 3x de R$ 33,34 (1ª) e R$ 33,33 e envia total_amount', async ({
    page,
  }) => {
    const envios = await simularCompras(page);
    await abrir(page);
    await valor(page).fill('100');
    await descricao(page).fill('Casa China');
    await data(page).fill('2026-01-28');
    await parcelas(page).selectOption('3');
    await expect(previa(page)).toHaveText(
      '3x de R$ 33,34 (1ª) e R$ 33,33 — primeira fatura: fevereiro',
    );
    await salvar(page).click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo).toEqual({
      description: 'Casa China',
      amount: '100',
      date: '2026-01-28',
      creditCardId: 'c-azul',
      installments: { total: 3, mode: 'total_amount' },
    });
  });

  test('US3.2: modo "Valor da parcela" 102,79 em 8x mostra o total R$ 822,32', async ({ page }) => {
    const envios = await simularCompras(page);
    await abrir(page);
    await valor(page).fill('102,79');
    await descricao(page).fill('Shoppe Casa');
    await parcelas(page).selectOption('8');
    await expect(modo(page, 'Valor total')).toHaveAttribute('aria-checked', 'true');
    await modo(page, 'Valor da parcela').click();
    await expect(previa(page)).toHaveText(
      '8x de R$ 102,79 (total R$ 822,32) — primeira fatura: março',
    );
    await salvar(page).click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo.installments).toEqual({ total: 8, mode: 'installment_amount' });
  });

  test('voltar para 1x volta à pré-visualização da fatura e envia sem parcelas', async ({
    page,
  }) => {
    const envios = await simularCompras(page);
    await abrir(page);
    await valor(page).fill('100');
    await descricao(page).fill('Casa China');
    await parcelas(page).selectOption('3');
    await expect(modo(page, 'Valor total')).toBeVisible();
    await parcelas(page).selectOption('1');
    await expect(modo(page, 'Valor total')).toHaveCount(0);
    await expect(previa(page)).toHaveText('Fatura de março — vence 25/03');
    await salvar(page).click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo.installments).toBeUndefined();
  });

  test('estorno parcelado é barrado antes de chamar a API', async ({ page }) => {
    const envios = await simularCompras(page);
    await abrir(page);
    await valor(page).fill('-50');
    await descricao(page).fill('Estorno');
    await parcelas(page).selectOption('3');
    await salvar(page).click();

    await expect(page.getByTestId('erro-parcelas')).toHaveText(
      'Estorno ou crédito é sempre à vista.',
    );
    expect(envios).toHaveLength(0);
  });

  test('US4.1: parcelamento em andamento 5 de 8, R$ 102,74, fatura 2026-11, cria só as restantes', async ({
    page,
  }) => {
    const envios = await simularCompras(page);
    await abrir(page);
    await valor(page).fill('102,74');
    await descricao(page).fill('Shoppe Casa');
    await andamento(page).check();
    await expect(page.getByLabel('Data da compra')).toBeVisible();
    await data(page).fill('2026-06-20');
    await parcelas(page).selectOption('8');
    await page.getByLabel('Próxima parcela').fill('5');
    await page.getByLabel('Fatura da parcela 5').fill('2026-11');
    await expect(previa(page)).toHaveText(
      '4 parcelas restantes (5/8 a 8/8) de R$ 102,74 — primeira fatura: novembro',
    );
    await salvar(page).click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo).toEqual({
      description: 'Shoppe Casa',
      amount: '102,74',
      date: '2026-06-20',
      creditCardId: 'c-azul',
      installments: { total: 8, mode: 'in_progress', nextNumber: 5, firstInvoiceMonth: '2026-11' },
    });
  });

  test('US4.1: a fatura da parcela p começa na fatura atual do cartão (FR-014)', async ({
    page,
  }) => {
    await simularCompras(page);
    await abrir(page);
    await andamento(page).check();
    // hoje 17/03, Azul fecha dia 18: a fatura atual é a de março
    await expect(page.locator('#lancamento-fatura-parcela')).toHaveValue('2026-03');
    // Verde fecha dia 1: a compra de 17/03 já cai na fatura de abril
    await cartao(page).selectOption({ label: 'Verde' });
    await expect(page.locator('#lancamento-fatura-parcela')).toHaveValue('2026-04');
  });

  test('US4.2: próxima parcela maior que o total impede salvar', async ({ page }) => {
    const envios = await simularCompras(page);
    await abrir(page);
    await valor(page).fill('102,74');
    await descricao(page).fill('Shoppe Casa');
    await andamento(page).check();
    await parcelas(page).selectOption('8');
    await page.getByLabel('Próxima parcela').fill('9');
    await salvar(page).click();

    await expect(page.getByTestId('erro-parcelas')).toHaveText(
      'A próxima parcela deve estar entre 1 e 8.',
    );
    expect(envios).toHaveLength(0);
    await expect(page.getByRole('dialog')).toBeVisible();
  });
});

// Contra a stack real (E2E_BASE_URL): o servidor gera as parcelas (RT-02) e as faturas.
test.describe('aba Cartão — parcelas (US3, US4) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  const criacao = (page: Page) =>
    page.waitForResponse(
      (r) => r.url().endsWith('/api/v1/card-purchases') && r.request().method() === 'POST',
    );

  async function prepararCartaoAzul(page: Page) {
    const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
    await page.goto('/criar-conta');
    await page.getByLabel('Nome', { exact: true }).fill('Lia Souza');
    await page.getByLabel('E-mail').fill(email);
    await page.getByLabel('Senha', { exact: true }).fill('senha-teste-123');
    await page.getByRole('button', { name: 'Criar conta' }).click();
    await dispensarGuiaSeAberto(page);

    await page.goto('/carteiras/nova');
    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Saldo inicial', { exact: true }).fill('500,00');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page).toHaveURL(/\/carteiras$/);

    await page.goto('/cartoes/novo');
    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Dia de fechamento').fill('18');
    await page.getByLabel('Dia de vencimento').fill('25');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page).toHaveURL(/\/cartoes$/);
    await page.goto('/');
    await page.getByTestId('lancar').click();
    await expect(cartao(page)).toBeVisible();
  }

  const resumo = (
    compras: { displayDescription: string; amountCents: number; dueMonth: string }[],
  ) => compras.map((c) => [c.displayDescription, c.amountCents, c.dueMonth]);

  test('US3.1: "Casa China" R$ 100,00 em 3x em 28/01 cria 3 parcelas nas faturas seguidas', async ({
    page,
  }) => {
    await prepararCartaoAzul(page);
    await valor(page).fill('100');
    await descricao(page).fill('Casa China');
    await data(page).fill('2026-01-28');
    await parcelas(page).selectOption('3');
    const resposta = criacao(page);
    await salvar(page).click();

    const { purchases } = await (await resposta).json();
    expect(resumo(purchases)).toEqual([
      ['Casa China 1/3', 3334, '2026-02'],
      ['Casa China 2/3', 3333, '2026-03'],
      ['Casa China 3/3', 3333, '2026-04'],
    ]);
  });

  test('US3.2: "Shoppe Casa" R$ 102,79 em 8x no modo parcela cria 8 parcelas iguais', async ({
    page,
  }) => {
    await prepararCartaoAzul(page);
    await valor(page).fill('102,79');
    await descricao(page).fill('Shoppe Casa');
    await data(page).fill('2026-03-10');
    await parcelas(page).selectOption('8');
    await modo(page, 'Valor da parcela').click();
    const resposta = criacao(page);
    await salvar(page).click();

    const { purchases } = await (await resposta).json();
    expect(purchases).toHaveLength(8);
    expect(purchases.every((c: { amountCents: number }) => c.amountCents === 10279)).toBe(true);
    expect(purchases[0].displayDescription).toBe('Shoppe Casa 1/8');
    expect(purchases[7].displayDescription).toBe('Shoppe Casa 8/8');
  });

  test('US4.1: "Shoppe Casa" 5 de 8 cria as parcelas 5/8 a 8/8 de 2026-11 a 2027-02', async ({
    page,
  }) => {
    await prepararCartaoAzul(page);
    await valor(page).fill('102,74');
    await descricao(page).fill('Shoppe Casa');
    await andamento(page).check();
    await data(page).fill('2026-06-20');
    await parcelas(page).selectOption('8');
    await page.getByLabel('Próxima parcela').fill('5');
    await page.getByLabel('Fatura da parcela 5').fill('2026-11');
    const resposta = criacao(page);
    await salvar(page).click();

    const { purchases } = await (await resposta).json();
    expect(resumo(purchases)).toEqual([
      ['Shoppe Casa 5/8', 10274, '2026-11'],
      ['Shoppe Casa 6/8', 10274, '2026-12'],
      ['Shoppe Casa 7/8', 10274, '2027-01'],
      ['Shoppe Casa 8/8', 10274, '2027-02'],
    ]);
    expect(purchases.every((c: { date: string }) => c.date === '2026-06-20')).toBe(true);
  });
});
