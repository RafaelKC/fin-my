import { expect, test, type Page } from '@playwright/test';
import { HOJE, TAGS, carteiraAzul, criarEspacoReal, sessao } from './tags-api';

// 004 T007 — tela de acertos e "Gerar cobrança" (US2, US3; FR-009, FR-010; research R4).
// API simulada: números do exemplo da US2 (Bruna em março/2026) e o formulário pré-preenchido.
// Stack real: o mesmo exemplo montado pela API, conferido pelo servidor.

const bruna = {
  tagId: 't-bruna',
  tagName: 'Bruna',
  tagColor: '#3E63DD',
  archived: false,
  spentCents: 20000,
  chargedCents: 18000,
  receivedCents: 3000,
  toChargeCents: 2000,
  toReceiveCents: 17000,
  accumulatedCents: 17000,
};
const carla = {
  ...bruna,
  tagId: 't-carla',
  tagName: 'Carla',
  spentCents: 3000,
  chargedCents: 5000,
  receivedCents: 5000,
  toChargeCents: -2000,
  toReceiveCents: -2000,
  accumulatedCents: -2000,
};

const itens = [
  {
    group: 'spent',
    type: 'card_purchase',
    id: 'p1',
    description: 'Ifood',
    date: '2026-03-05',
    signedCents: 15681,
  },
  {
    group: 'spent',
    type: 'card_purchase',
    id: 'p2',
    description: 'Uber',
    date: '2026-03-06',
    signedCents: 1272,
  },
  {
    group: 'spent',
    type: 'card_purchase',
    id: 'p3',
    description: 'Estorno',
    date: '2026-03-07',
    signedCents: -953,
  },
  {
    group: 'spent',
    type: 'expense',
    id: 'e1',
    description: 'Presente Bruna',
    date: '2026-03-08',
    signedCents: 4000,
  },
  {
    group: 'charged',
    type: 'income',
    id: 'e2',
    description: 'Bruna',
    date: '2026-03-12',
    signedCents: 15000,
    received: false,
  },
  {
    group: 'charged',
    type: 'income',
    id: 'e3',
    description: 'Bruna Pix',
    date: '2026-03-09',
    signedCents: 3000,
    received: true,
  },
];

async function simular(page: Page): Promise<object[]> {
  const envios: object[] = [];
  let cobrada = false;
  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteiraAzul] }));
  await page.route('**/api/v1/tags**', (route) => route.fulfill({ json: TAGS }));
  await page.route('**/api/v1/credit-cards**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/entries/suggestions**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/settlements/2026-03', (route) =>
    route.fulfill({
      json: [{ ...bruna, ...(cobrada ? { toChargeCents: 0, chargedCents: 20000 } : {}) }, carla],
    }),
  );
  await page.route('**/api/v1/settlements/2026-03/tags/t-bruna/items', (route) =>
    route.fulfill({ json: itens }),
  );
  await page.route('**/api/v1/entries', async (route) => {
    const req = route.request();
    if (req.method() !== 'POST') return route.fulfill({ json: [] });
    const corpo = req.postDataJSON() as Record<string, unknown>;
    envios.push(corpo);
    cobrada = true;
    return route.fulfill({
      status: 201,
      json: {
        id: 'nova',
        kind: 'income',
        description: corpo['description'],
        amountCents: 2000,
        date: corpo['date'],
        referenceMonth: corpo['referenceMonth'] ?? '2026-03',
        status: 'planned',
        walletId: 'w-azul',
        createdAt: '',
        version: 1,
        tagIds: corpo['tagIds'],
        settlementTagId: 't-bruna',
      },
    });
  });
  return envios;
}

const cartao = (page: Page, nome: string) =>
  page.locator('[data-testid="acerto"]', { hasText: nome });

test.describe('acertos (US2, US3) — API simulada', () => {
  test('US2.1: "Acertos" na barra leva ao mês de hoje com os números da Bruna', async ({
    page,
  }) => {
    await simular(page);
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Principal' });
    await nav.getByRole('link', { name: 'Acertos' }).click();
    await expect(page).toHaveURL(/\/acertos\/2026-03$/);
    await expect(page.getByTestId('acertos-titulo')).toHaveText('Março de 2026');

    const b = cartao(page, 'Bruna');
    await expect(b.getByTestId('numero-spent')).toContainText('200,00');
    await expect(b.getByTestId('numero-charged')).toContainText('180,00');
    await expect(b.getByTestId('numero-received')).toContainText('30,00');
    await expect(b.getByTestId('a-cobrar')).toContainText('20,00');
    await expect(b.getByTestId('a-receber')).toContainText('170,00');
    await expect(b.getByTestId('saldo')).toContainText('170,00');
  });

  test('US2.3: saldo negativo vira "Crédito da Carla"; sem A cobrar não há botão (US3.3)', async ({
    page,
  }) => {
    await simular(page);
    await page.goto('/acertos/2026-03');
    const c = cartao(page, 'Carla');
    await expect(c.getByTestId('saldo')).toContainText('Crédito da Carla: R$');
    await expect(c.getByTestId('saldo')).toContainText('20,00');
    await expect(c.getByTestId('gerar-cobranca')).toHaveCount(0);
    await expect(cartao(page, 'Bruna').getByTestId('gerar-cobranca')).toBeVisible();
  });

  test('tocar num número abre os lançamentos que o compõem (FR-009)', async ({ page }) => {
    await simular(page);
    await page.goto('/acertos/2026-03');
    const b = cartao(page, 'Bruna');
    await b.getByTestId('numero-spent').click();
    await expect(b.getByTestId('item')).toHaveCount(4);
    await expect(b.getByTestId('item').first()).toContainText('Ifood');
    await expect(b.getByTestId('item').first()).toContainText('156,81');

    await b.getByTestId('numero-received').click();
    await expect(b.getByTestId('item')).toHaveCount(1);
    await expect(b.getByTestId('item')).toContainText('Bruna Pix');

    await b.getByTestId('numero-charged').click();
    await expect(b.getByTestId('item')).toHaveCount(2);
  });

  test('US3.1/US3.4: "Gerar cobrança" abre o formulário pré-preenchido; salvar zera A cobrar', async ({
    page,
  }) => {
    const envios = await simular(page);
    await page.goto('/acertos/2026-03');
    await cartao(page, 'Bruna').getByTestId('gerar-cobranca').click();

    // hoje é 10/03/2026 (mês dos acertos): data = hoje
    await expect(page.locator('#lancamento-descricao')).toHaveValue('Bruna — acerto 03/2026');
    await expect(page.locator('#amount-field')).toHaveValue(/20,00/);
    await expect(page.locator('#lancamento-data')).toHaveValue('2026-03-10');
    await expect(page.locator('#lancamento-carteira')).toHaveValue('w-azul');
    expect(envios).toHaveLength(0); // nada é salvo sem "Salvar"

    // US3.4: o que a pessoa digita prevalece
    await page.locator('#amount-field').fill('25,00');
    await page.getByTestId('salvar').click();
    await expect.poll(() => envios.length).toBe(1);
    expect(envios[0]).toMatchObject({
      kind: 'income',
      description: 'Bruna — acerto 03/2026',
      amount: '25,00',
      date: '2026-03-10',
      status: 'planned',
      tagIds: ['t-bruna'],
    });
    await expect(cartao(page, 'Bruna').getByTestId('a-cobrar')).toContainText('0,00');
    await expect(cartao(page, 'Bruna').getByTestId('gerar-cobranca')).toHaveCount(0);
  });

  test('as setas trocam de mês', async ({ page }) => {
    await simular(page);
    await page.route('**/api/v1/settlements/2026-04', (route) => route.fulfill({ json: [] }));
    await page.goto('/acertos/2026-03');
    await page.getByTestId('acertos-seguinte').click();
    await expect(page).toHaveURL(/\/acertos\/2026-04$/);
    await expect(page.getByTestId('acertos-titulo')).toHaveText('Abril de 2026');
    await expect(page.getByTestId('sem-acertos')).toBeVisible();
  });
});

test.describe('acertos (US2, US3, FR-011) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa do compose no ar e de E2E_BASE_URL');

  test('exemplo da US2 no servidor, Gerar cobrança e "Por pessoa" na fatura', async ({ page }) => {
    const api = await criarEspacoReal(page);
    const brunaTag = await api.tag('Bruna', true);
    const cartaoVerde = await api.cartao('Verde', 18, 25);
    const envio = async (caminho: string, data: object) => {
      const r = await page.request.post(`/api/v1${caminho}`, {
        headers: { ...api.auth, 'Idempotency-Key': crypto.randomUUID() },
        data,
      });
      expect(r.status(), `${caminho}: ${await r.text()}`).toBe(201);
    };
    // fatura Verde 2026-03: compras de 05–10/03 (fecha dia 18, vence dia 25)
    await api.compra(cartaoVerde.id, 'Ifood', '156,81', '2026-03-05', [brunaTag.id]);
    await api.compra(cartaoVerde.id, 'Uber', '12,72', '2026-03-06', [brunaTag.id]);
    await api.compra(cartaoVerde.id, 'Estorno', '-9,53', '2026-03-07', [brunaTag.id]);
    await api.compra(cartaoVerde.id, 'Mercado', '50,00', '2026-03-08');
    const entrada = (
      kind: string,
      description: string,
      amount: string,
      status: string,
      date: string,
    ) =>
      envio('/entries', {
        kind,
        description,
        amount,
        date,
        walletId: api.walletId,
        status,
        referenceMonth: '2026-03',
        tagIds: [brunaTag.id],
      });
    await entrada('expense', 'Presente Bruna', '40,00', 'settled', '2026-03-09');
    await entrada('income', 'Bruna', '150,00', 'planned', '2026-03-12');
    await entrada('income', 'Bruna Pix', '30,00', 'settled', '2026-03-09');

    await page.goto('/acertos/2026-03');
    const b = cartao(page, 'Bruna');
    await expect(b.getByTestId('numero-spent')).toContainText('200,00');
    await expect(b.getByTestId('numero-charged')).toContainText('180,00');
    await expect(b.getByTestId('numero-received')).toContainText('30,00');
    await expect(b.getByTestId('a-cobrar')).toContainText('20,00');
    await expect(b.getByTestId('a-receber')).toContainText('170,00');
    await expect(b.getByTestId('saldo')).toContainText('170,00');

    await b.getByTestId('numero-spent').click();
    await expect(b.getByTestId('item')).toHaveCount(4);

    // US3.1/US3.2: mês que não é o de hoje → dia 1; salvar zera A cobrar
    await b.getByTestId('gerar-cobranca').click();
    await expect(page.locator('#lancamento-descricao')).toHaveValue('Bruna — acerto 03/2026');
    await expect(page.locator('#amount-field')).toHaveValue(/20,00/);
    await expect(page.locator('#lancamento-data')).toHaveValue('2026-03-01');
    await page.getByTestId('salvar').click();
    await expect(b.getByTestId('a-cobrar')).toContainText('0,00');
    await expect(b.getByTestId('gerar-cobranca')).toHaveCount(0);
    await expect(b.getByTestId('saldo')).toContainText('170,00');

    // FR-011 / SC-004: "Por pessoa" soma o Total da fatura
    await page.goto(`/cartoes/${cartaoVerde.id}/faturas/2026-03`);
    const pessoas = page.getByTestId('subtotal-pessoa');
    await expect(pessoas).toHaveCount(2);
    await expect(pessoas.filter({ hasText: 'Bruna' })).toContainText('160,00');
    await expect(pessoas.filter({ hasText: 'Sem tag de acerto' })).toContainText('50,00');
    await expect(page.getByTestId('total')).toContainText('210,00');
  });
});
