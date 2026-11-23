import { expect, test, type Page } from '@playwright/test';
import { simularCompras } from './compras-api';

// 003 T007 — aba "Cartão" do formulário rápido: compra à vista (US2) e confirmação de fatura paga
// (US5.7). Parcelas ficam em `parcelas.spec.ts`.

const abrir = async (page: Page) => {
  await page.goto('/');
  await page.getByTestId('lancar').click();
  await expect(page.getByRole('radiogroup', { name: 'Tipo de lançamento' })).toBeVisible();
};
const valor = (page: Page) => page.getByLabel('Valor', { exact: true });
const descricao = (page: Page) => page.getByRole('textbox', { name: 'Descrição' });
const cartao = (page: Page) => page.getByRole('combobox', { name: 'Cartão' });
const data = (page: Page) => page.locator('#lancamento-data');
const previa = (page: Page) => page.getByTestId('previa-fatura');
const salvar = (page: Page) => page.getByTestId('salvar');

test.describe('aba Cartão — compra à vista (US2) — API simulada', () => {
  test('primeiro uso abre em Cartão, com o primeiro cartão ativo em ordem alfabética e a fatura (FR-010)', async ({
    page,
  }) => {
    await simularCompras(page);
    await abrir(page);

    await expect(page.getByRole('radio', { name: 'Cartão', exact: true })).toHaveAttribute(
      'aria-checked',
      'true',
    );
    await expect(cartao(page)).toHaveValue('c-azul');
    await expect(page.getByRole('radio', { name: 'Prevista' })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Carteira' })).toHaveCount(0);
    await expect(previa(page)).toHaveText('Fatura de março — vence 25/03');
  });

  test('US2.1 e SC-001: "Uber" 12,95 em 17/03 cai na fatura de março; salvar leva 2 toques e menos de 15 s', async ({
    page,
  }) => {
    const envios = await simularCompras(page);
    const inicio = Date.now();
    await page.goto('/');
    await page.getByTestId('lancar').click(); // toque 1
    await valor(page).fill('12,95');
    await descricao(page).fill('Uber');
    await expect(previa(page)).toHaveText('Fatura de março — vence 25/03');
    await salvar(page).click(); // toque 2

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(Date.now() - inicio).toBeLessThan(15_000);
    expect(envios).toHaveLength(1);
    expect(envios[0].corpo).toEqual({
      description: 'Uber',
      amount: '12,95',
      date: '2026-03-17',
      creditCardId: 'c-azul',
    });
    expect(envios[0].chave).toMatch(/^[0-9a-f-]{36}$/);
  });

  test('regra de fatura: no dia do fechamento vai para a próxima; cartão Verde fecha dia 1', async ({
    page,
  }) => {
    await simularCompras(page);
    await abrir(page);

    await data(page).fill('2026-03-18');
    await expect(previa(page)).toHaveText('Fatura de abril — vence 25/04');
    await data(page).fill('2026-03-17');
    await expect(previa(page)).toHaveText('Fatura de março — vence 25/03');

    await cartao(page).selectOption({ label: 'Verde' });
    await data(page).fill('2026-01-31');
    await expect(previa(page)).toHaveText('Fatura de fevereiro — vence 10/02');
    await data(page).fill('2026-02-01');
    await expect(previa(page)).toHaveText('Fatura de março — vence 10/03');
  });

  test('US2.2: -99,90 avisa "Lançado como estorno/crédito" e envia o valor como digitado', async ({
    page,
  }) => {
    const envios = await simularCompras(page);
    await abrir(page);
    await valor(page).fill('-99,90');
    await descricao(page).fill('Estorno Óculos');
    await expect(page.getByTestId('aviso-credito')).toBeVisible();
    await salvar(page).click();

    await expect(page.getByText('Lançado como estorno/crédito.', { exact: true })).toBeVisible();
    expect(envios[0].corpo.amount).toBe('-99,90');
  });

  test('US2.3: 239,8/2 mostra R$ 119,90 e envia a expressão', async ({ page }) => {
    const envios = await simularCompras(page);
    await abrir(page);
    await valor(page).fill('239,8/2');
    await expect(page.getByText('= R$ 119,90')).toBeVisible();
    await descricao(page).fill('Mercado');
    await salvar(page).click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo.amount).toBe('239,8/2');
  });

  test('sugestão aplica o cartão da compra mais recente, mas não troca o escolhido à mão (FR-011)', async ({
    page,
  }) => {
    await simularCompras(page, {
      sugestoes: { ube: [{ description: 'Uber', uses: 5, creditCardId: 'c-verde' }] },
    });
    await abrir(page);
    await descricao(page).fill('Ube');
    await page.getByTestId('sugestao').click();
    await expect(descricao(page)).toHaveValue('Uber');
    await expect(cartao(page)).toHaveValue('c-verde');

    await cartao(page).selectOption({ label: 'Azul' });
    await descricao(page).fill('Ube');
    await page.getByTestId('sugestao').click();
    await expect(cartao(page)).toHaveValue('c-azul');
  });

  test('lembra o último cartão usado neste aparelho (R9)', async ({ page }) => {
    await simularCompras(page);
    await abrir(page);
    await cartao(page).selectOption({ label: 'Verde' });
    await valor(page).fill('30');
    await descricao(page).fill('Padaria');
    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await page.getByTestId('lancar').click();
    await expect(cartao(page)).toHaveValue('c-verde');
  });

  test('US5.7: fatura já paga pede confirmação e só lança com "Lançar mesmo assim"', async ({
    page,
  }) => {
    const envios = await simularCompras(page, { faturaPaga: 2 });
    await abrir(page);
    await valor(page).fill('50');
    await descricao(page).fill('Lanche');
    await salvar(page).click();

    const dialogo = page.getByRole('dialog').filter({ hasText: 'Fatura já paga' });
    await expect(dialogo).toContainText('Esta fatura já foi paga e voltará a ter valor em aberto');
    await dialogo.getByRole('button', { name: 'Cancelar' }).click();
    expect(envios).toHaveLength(0);
    await expect(descricao(page)).toHaveValue('Lanche');

    await salvar(page).click();
    await page
      .getByRole('dialog')
      .filter({ hasText: 'Fatura já paga' })
      .getByRole('button', { name: 'Lançar mesmo assim' })
      .click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios).toHaveLength(1);
    expect(envios[0].corpo.confirmPaidInvoice).toBe(true);
  });

  test('sem cartões, avisa e leva ao cadastro', async ({ page }) => {
    await simularCompras(page);
    await page.route('**/api/v1/credit-cards**', (route) => route.fulfill({ json: [] }));
    await abrir(page);
    await expect(page.getByTestId('sem-cartoes')).toBeVisible();
    await page.getByTestId('novo-cartao').click();
    await expect(page).toHaveURL(/\/cartoes\/novo$/);
  });
});

// Contra a stack real (E2E_BASE_URL): o servidor aplica a regra de fatura (RT-03), RT-05 e R6.
test.describe('aba Cartão — compra à vista (US2) — stack real', () => {
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
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);

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

  test('US2.1, US2.4 e US2.2: Uber na fatura de março, 18/03 na de abril, estorno como crédito', async ({
    page,
  }) => {
    await prepararCartaoAzul(page);

    await valor(page).fill('12,95');
    await descricao(page).fill('Uber');
    await data(page).fill('2026-03-17');
    await expect(previa(page)).toHaveText('Fatura de março — vence 25/03');
    const primeira = criacao(page);
    await salvar(page).click();
    const uber = (await (await primeira).json()).purchases[0];
    expect(uber).toMatchObject({ dueMonth: '2026-03', amountCents: 1295, nature: 'debit' });
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await page.getByTestId('lancar').click();
    await valor(page).fill('20');
    await descricao(page).fill('Uber');
    await data(page).fill('2026-03-18');
    await expect(previa(page)).toHaveText('Fatura de abril — vence 25/04');
    const segunda = criacao(page);
    await salvar(page).click();
    expect((await (await segunda).json()).purchases[0].dueMonth).toBe('2026-04');
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await page.getByTestId('lancar').click();
    await valor(page).fill('-99,90');
    await descricao(page).fill('Estorno Óculos');
    await data(page).fill('2026-03-10');
    const terceira = criacao(page);
    await salvar(page).click();
    const resposta = await (await terceira).json();
    expect(resposta.warnings).toEqual(['recorded_as_credit']);
    expect(resposta.purchases[0]).toMatchObject({ nature: 'credit', amountCents: 9990 });
    await expect(page.getByText('Lançado como estorno/crédito.', { exact: true })).toBeVisible();
  });

  test('US2.3: 239,8/2 vira R$ 119,90 no servidor', async ({ page }) => {
    await prepararCartaoAzul(page);
    await valor(page).fill('239,8/2');
    await descricao(page).fill('Mercado');
    await data(page).fill('2026-03-10');
    const resposta = criacao(page);
    await salvar(page).click();
    expect((await (await resposta).json()).purchases[0].amountCents).toBe(11990);
  });

  test('a sugestão de "Uber" traz o cartão da última compra (R9)', async ({ page }) => {
    await prepararCartaoAzul(page);
    await valor(page).fill('12,95');
    await descricao(page).fill('Uber');
    await data(page).fill('2026-03-10');
    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await page.getByTestId('lancar').click();
    await descricao(page).fill('Ub');
    await expect(page.getByTestId('sugestao').filter({ hasText: 'Uber' })).toBeVisible();
    await page.getByTestId('sugestao').filter({ hasText: 'Uber' }).click();
    await expect(cartao(page).locator('option:checked')).toHaveText('Azul');
  });
});
