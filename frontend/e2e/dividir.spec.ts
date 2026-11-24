import { expect, test, type Page } from '@playwright/test';
import { comecarEmSaida } from './navegacao';
import { HOJE, TAGS, carteiraAzul, criarEspacoReal, sessao } from './tags-api';

// 004 T009 — "Dividir" no formulário rápido (US4.1–US4.5; FR-012..FR-014). A API simulada guarda o
// que o app envia; a stack real confere a divisão (RT-02), as partes independentes e o selo.

interface Envios {
  entradas: { split: { mode: string; parts: Record<string, unknown>[] } }[];
  compras: Record<string, unknown>[];
}

async function simular(page: Page): Promise<Envios> {
  const envios: Envios = { entradas: [], compras: [] };
  await comecarEmSaida(page);
  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteiraAzul] }));
  await page.route('**/api/v1/credit-cards**', (route) =>
    route.fulfill({
      json: [
        {
          id: 'c-verde',
          name: 'Verde',
          closingDay: 18,
          dueDay: 25,
          paymentWalletId: 'w-azul',
          color: '#43A047',
          limitCents: null,
          archived: false,
          hasPurchases: false,
          version: 1,
        },
      ],
    }),
  );
  await page.route('**/api/v1/entries/suggestions**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/tags**', (route) => route.fulfill({ json: TAGS }));
  await page.route('**/api/v1/entries/split', async (route) => {
    const corpo = route.request().postDataJSON();
    envios.entradas.push(corpo);
    return route.fulfill({
      status: 201,
      json: corpo.split.parts.map((p: Record<string, unknown>, i: number) => ({
        id: `e${i}`,
        kind: 'expense',
        description: corpo.description,
        amountCents: 100,
        date: corpo.date,
        referenceMonth: String(corpo.date).slice(0, 7),
        status: 'settled',
        walletId: corpo.walletId,
        createdAt: '2026-03-10T15:00:00Z',
        version: 1,
        tagIds: [],
        settlementTagId: p['settlementTagId'],
        splitGroupId: 'g1',
        splitCount: corpo.split.parts.length,
      })),
    });
  });
  await page.route('**/api/v1/card-purchases/split', async (route) => {
    envios.compras.push(route.request().postDataJSON());
    return route.fulfill({ status: 201, json: { purchases: [], warnings: [] } });
  });
  return envios;
}

const abrir = async (page: Page) => {
  await page.goto('/');
  await page.getByTestId('lancar').click();
  await expect(page.getByRole('radiogroup', { name: 'Tipo de lançamento' })).toBeVisible();
};
const valor = (page: Page) => page.getByLabel('Valor', { exact: true });
const descricao = (page: Page) => page.getByRole('textbox', { name: 'Descrição' });
const salvar = (page: Page) => page.getByTestId('salvar');
const tipo = (page: Page, nome: string) =>
  page.getByRole('radio', { name: nome, exact: true }).click();
const dividir = (page: Page) => page.getByTestId('dividir').click();
const pessoa = (page: Page, parte: number) => page.locator(`#parte-pessoa-${parte}`);
const valoresCalculados = (page: Page) => page.getByTestId('parte-valor-calculado');
const brl = (texto: string | null) => (texto ?? '').replace(/\s/g, ' ').trim();

test.describe('Dividir (US4) — API simulada', () => {
  test('US4.1: R$ 239,80 entre Eu e Bruna em partes iguais (Saída)', async ({ page }) => {
    const envios = await simular(page);
    await abrir(page);
    await valor(page).fill('239,80');
    await descricao(page).fill('Jacomar Niver Carla');
    await dividir(page);

    await expect(valoresCalculados(page)).toHaveCount(2);
    for (const v of await valoresCalculados(page).allTextContents()) {
      expect(brl(v)).toBe('R$ 119,90');
    }
    await expect(pessoa(page, 0)).toHaveValue('');
    await expect(pessoa(page, 1)).toHaveValue('t-bruna');

    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios.entradas).toHaveLength(1);
    expect(envios.entradas[0].split).toEqual({
      mode: 'equal',
      parts: [{ settlementTagId: null }, { settlementTagId: 't-bruna' }],
    });
  });

  test('US4.4: R$ 100,00 em 3 partes iguais → 33,34 / 33,33 / 33,33', async ({ page }) => {
    await simular(page);
    await abrir(page);
    await valor(page).fill('100');
    await descricao(page).fill('Presente');
    await dividir(page);
    await page.getByTestId('parte-adicionar').click();
    await expect(valoresCalculados(page)).toHaveCount(3);
    const textos = (await valoresCalculados(page).allTextContents()).map(brl);
    expect(textos).toEqual(['R$ 33,34', 'R$ 33,33', 'R$ 33,33']);
  });

  test('US4.3: valores informados mostram a diferença e só salvam quando fecha', async ({
    page,
  }) => {
    const envios = await simular(page);
    await abrir(page);
    await valor(page).fill('100');
    await descricao(page).fill('Presente');
    await dividir(page);
    await page.getByTestId('modo-amounts').click();
    await page.getByLabel('Valor da parte 1').fill('40');
    await page.getByLabel('Valor da parte 2').fill('50');
    await expect(page.getByTestId('erro-divisao')).toHaveText('Faltam R$ 10,00');
    await expect(salvar(page)).toBeDisabled();

    await page.getByLabel('Valor da parte 2').fill('70');
    await expect(page.getByTestId('erro-divisao')).toHaveText('Sobram R$ 10,00');
    await expect(salvar(page)).toBeDisabled();

    await page.getByLabel('Valor da parte 2').fill('60');
    await expect(page.getByTestId('erro-divisao')).toHaveCount(0);
    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios.entradas[0].split.parts).toEqual([
      { settlementTagId: null, amount: '40' },
      { settlementTagId: 't-bruna', amount: '60' },
    ]);
  });

  test('Cartão: compra parcelada dividida vai para /card-purchases/split com as parcelas', async ({
    page,
  }) => {
    const envios = await simular(page);
    await abrir(page);
    await tipo(page, 'Cartão');
    await valor(page).fill('207,90');
    await descricao(page).fill('MYNO');
    await page.getByLabel('Parcelas').selectOption('4');
    await dividir(page);
    await page.getByTestId('parte-adicionar').click();
    await page.getByTestId('modo-amounts').click();
    await page.getByLabel('Valor da parte 1').fill('110,94');
    await page.getByLabel('Valor da parte 2').fill('11,99');
    await page.getByLabel('Valor da parte 3').fill('84,97');
    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);

    expect(envios.compras).toHaveLength(1);
    expect(envios.compras[0]['installments']).toEqual({ total: 4, mode: 'total_amount' });
    expect(envios.compras[0]['split']).toMatchObject({ mode: 'amounts' });
  });

  test('tag de acerto escolhida antes vira a pessoa da segunda parte (atalho do aviso)', async ({
    page,
  }) => {
    const envios = await simular(page);
    await abrir(page);
    await valor(page).fill('50');
    await descricao(page).fill('Almoço');
    await page.locator('[data-testid="tag-chip"][data-tag="Bruna"]').click();
    await page.getByTestId('tag-busca').fill('Carla');
    await page.getByTestId('tag-resultado').click();
    await page.getByTestId('tag-dividir').click();
    await expect(page.getByTestId('dividir-painel')).toBeVisible();
    await expect(pessoa(page, 1)).toHaveValue('t-bruna');
    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios.entradas[0].split.parts[1]).toEqual({ settlementTagId: 't-bruna' });
  });

  test('Entrada e Transferência não oferecem Dividir', async ({ page }) => {
    await simular(page);
    await abrir(page);
    await tipo(page, 'Entrada');
    await expect(page.getByTestId('dividir')).toHaveCount(0);
    await tipo(page, 'Transferência');
    await expect(page.getByTestId('dividir')).toHaveCount(0);
  });
});

interface CompraResposta {
  id: string;
  dueMonth: string;
  amountCents: number;
  settlementTagId: string | null;
  splitCount: number;
  installmentNumber: number | null;
  installmentPlanId: string | null;
}

// Contra a stack real (E2E_BASE_URL): o servidor divide (RT-02), grava as partes e devolve o grupo.
test.describe('Dividir (US4) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  test('US4.1/US4.5: compra de R$ 239,80 dividida; selo na fatura; editar/excluir uma parte', async ({
    page,
  }) => {
    await comecarEmSaida(page);
    const api = await criarEspacoReal(page);
    const bruna = await api.tag('Bruna', true);
    const verde = await api.cartao('Verde', 18, 25);

    await page.goto('/');
    await page.getByTestId('lancar').click();
    await tipo(page, 'Cartão');
    await valor(page).fill('239,80');
    await descricao(page).fill('Jacomar Niver Carla');
    await dividir(page);
    await expect(pessoa(page, 1)).toHaveValue(bruna.id);

    const resposta = page.waitForResponse(
      (r) => r.url().endsWith('/card-purchases/split') && r.request().method() === 'POST',
    );
    await salvar(page).click();
    const { purchases } = (await (await resposta).json()) as { purchases: CompraResposta[] };
    await expect(page.getByRole('dialog')).toHaveCount(0);

    expect(purchases.map((p) => p.amountCents)).toEqual([11990, 11990]);
    expect(purchases.map((p) => p.settlementTagId)).toEqual([null, bruna.id]);
    expect(purchases.every((p) => p.splitCount === 2)).toBe(true);

    // selo "Dividida em 2" nas duas compras da fatura
    await page.goto(`/cartoes/${verde.id}/faturas/${purchases[0].dueMonth}`);
    await expect(page.getByTestId('selo-divisao')).toHaveText([/Dividida em 2/, /Dividida em 2/]);

    // US4.5: excluir uma parte não mexe na outra
    const excluida = await page.request.delete(`/api/v1/card-purchases/${purchases[0].id}`, {
      headers: api.auth,
    });
    expect(excluida.ok()).toBe(true);
    const outra = await page.request.get(`/api/v1/card-purchases/${purchases[1].id}`, {
      headers: api.auth,
    });
    expect(outra.ok()).toBe(true);
    expect((await outra.json()).amountCents).toBe(11990);
  });

  test('US4.2: MYNO em 4x dividida com valores informados cria 3 parcelamentos', async ({
    page,
  }) => {
    await comecarEmSaida(page);
    const api = await criarEspacoReal(page);
    const bruna = await api.tag('Bruna', true);
    const carla = await api.tag('Carla', true);
    await api.cartao('Verde', 18, 25);

    await page.goto('/');
    await page.getByTestId('lancar').click();
    await tipo(page, 'Cartão');
    await valor(page).fill('207,90');
    await descricao(page).fill('MYNO');
    await page.getByLabel('Parcelas').selectOption('4');
    await dividir(page);
    await page.getByTestId('parte-adicionar').click();
    await page.getByTestId('modo-amounts').click();
    await page.getByLabel('Valor da parte 1').fill('110,94');
    // as duas últimas partes recebem as tags livres na ordem do servidor: Carla 11,99 e Bruna 84,97
    for (const parte of [2, 3]) {
      const id = await pessoa(page, parte - 1).inputValue();
      await page.getByLabel(`Valor da parte ${parte}`).fill(id === carla.id ? '11,99' : '84,97');
    }
    expect([bruna.id, carla.id]).toContain(await pessoa(page, 1).inputValue());

    const resposta = page.waitForResponse(
      (r) => r.url().endsWith('/card-purchases/split') && r.request().method() === 'POST',
    );
    await salvar(page).click();
    const { purchases } = (await (await resposta).json()) as { purchases: CompraResposta[] };
    await expect(page.getByRole('dialog')).toHaveCount(0);

    expect(new Set(purchases.map((p) => p.installmentPlanId)).size).toBe(3);
    expect(purchases).toHaveLength(12);
    const eu = purchases
      .filter((p) => p.settlementTagId === null)
      .sort((a, b) => (a.installmentNumber ?? 0) - (b.installmentNumber ?? 0));
    expect(eu.map((p) => p.amountCents)).toEqual([2775, 2773, 2773, 2773]);
    const total = (tagId: string) =>
      purchases.filter((p) => p.settlementTagId === tagId).reduce((s, p) => s + p.amountCents, 0);
    expect(total(carla.id)).toBe(1199);
    expect(total(bruna.id)).toBe(8497);
  });

  test('US4.4 e US4.3: Saída de R$ 100,00 em 3 partes; soma diferente não salva; selo no mês', async ({
    page,
  }) => {
    await comecarEmSaida(page);
    const api = await criarEspacoReal(page);
    await api.tag('Bruna', true);
    await api.tag('Carla', true);

    await page.goto('/');
    await page.getByTestId('lancar').click();
    await valor(page).fill('100');
    await descricao(page).fill('Presente');
    await dividir(page);
    await page.getByTestId('parte-adicionar').click();

    // US4.3: valores que não fecham bloqueiam o salvar
    await page.getByTestId('modo-amounts').click();
    await page.getByLabel('Valor da parte 1').fill('30');
    await page.getByLabel('Valor da parte 2').fill('30');
    await page.getByLabel('Valor da parte 3').fill('30');
    await expect(page.getByTestId('erro-divisao')).toHaveText('Faltam R$ 10,00');
    await expect(salvar(page)).toBeDisabled();

    // US4.4: partes iguais
    await page.getByTestId('modo-equal').click();
    await expect(valoresCalculados(page)).toHaveCount(3);
    const textos = (await valoresCalculados(page).allTextContents()).map(brl);
    expect(textos).toEqual(['R$ 33,34', 'R$ 33,33', 'R$ 33,33']);

    const resposta = page.waitForResponse(
      (r) => r.url().endsWith('/entries/split') && r.request().method() === 'POST',
    );
    await salvar(page).click();
    const entradas = (await (await resposta).json()) as {
      amountCents: number;
      referenceMonth: string;
      splitCount: number;
    }[];
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(entradas.map((e) => e.amountCents)).toEqual([3334, 3333, 3333]);
    expect(entradas.every((e) => e.splitCount === 3)).toBe(true);

    await page.goto(`/mes/${entradas[0].referenceMonth}`);
    await expect(page.getByTestId('selo-divisao')).toHaveCount(3);
    await expect(page.getByTestId('selo-divisao').first()).toHaveText(/Dividida em 3/);

    // FR-014: tocar no selo do formulário de edição mostra as outras duas partes
    await page.getByRole('button', { name: 'Presente' }).first().click();
    await page.getByRole('dialog').getByTestId('selo-divisao').click();
    await expect(page.getByTestId('outra-parte')).toHaveCount(2);
  });
});
