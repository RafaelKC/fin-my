import { expect, test, type Page, type Route } from '@playwright/test';
import { comecarEmSaida } from './navegacao';
import {
  HOJE,
  TAGS,
  carteiraAzul,
  criarEspacoReal,
  problema,
  sessao,
  tag,
  type TagSimulada,
} from './tags-api';

// 004 T004 — campo Tags do formulário rápido (US1.1–US1.4; FR-003; SC-001). A API simulada guarda o
// que o app envia; a stack real confere as regras no servidor (tags de acerto, nome normalizado).

interface Envios {
  entradas: Record<string, unknown>[];
  compras: Record<string, unknown>[];
  tagsCriadas: Record<string, unknown>[];
}

interface Opcoes {
  tags?: TagSimulada[];
  /** Respostas de `GET /entries/suggestions` por texto digitado. */
  sugestoes?: Record<string, object[]>;
}

async function simular(page: Page, opcoes: Opcoes = {}): Promise<Envios> {
  const envios: Envios = { entradas: [], compras: [], tagsCriadas: [] };
  const tags = [...(opcoes.tags ?? TAGS)];

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
  await page.route('**/api/v1/entries/suggestions**', (route) => {
    const q = new URL(route.request().url()).searchParams.get('q') ?? '';
    return route.fulfill({ json: opcoes.sugestoes?.[q.toLowerCase()] ?? [] });
  });
  await page.route('**/api/v1/tags**', async (route: Route) => {
    const req = route.request();
    if (req.method() !== 'POST') return route.fulfill({ json: tags });
    const corpo = req.postDataJSON() as { name: string };
    envios.tagsCriadas.push(corpo);
    const nova = tag(`t-${tags.length + 1}`, corpo.name, 0);
    tags.push(nova);
    return route.fulfill({ status: 201, json: nova });
  });
  await page.route('**/api/v1/entries', async (route) => {
    const req = route.request();
    if (req.method() !== 'POST') return route.fulfill({ json: [] });
    const corpo = req.postDataJSON() as Record<string, unknown>;
    envios.entradas.push(corpo);
    return route.fulfill({
      status: 201,
      json: {
        id: `e${envios.entradas.length}`,
        kind: corpo['kind'],
        description: corpo['description'],
        amountCents: 4499,
        date: corpo['date'],
        referenceMonth: String(corpo['date']).slice(0, 7),
        status: 'settled',
        walletId: corpo['walletId'],
        createdAt: '2026-03-10T15:00:00Z',
        version: 1,
        tagIds: corpo['tagIds'] ?? [],
        settlementTagId: null,
      },
    });
  });
  await page.route('**/api/v1/card-purchases', async (route) => {
    const req = route.request();
    if (req.method() !== 'POST') return route.fulfill({ json: [] });
    envios.compras.push(req.postDataJSON() as Record<string, unknown>);
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
const chips = (page: Page) => page.getByTestId('tag-chip');
const chip = (page: Page, nome: string) =>
  page.locator(`[data-testid="tag-chip"][data-tag="${nome}"]`);
const busca = (page: Page) => page.getByTestId('tag-busca');
const tipo = (page: Page, nome: string) =>
  page.getByRole('radio', { name: nome, exact: true }).click();

test.describe('campo Tags (US1) — API simulada', () => {
  test('US1.1: as 8 tags mais usadas são botões na ordem de uso e há campo de busca', async ({
    page,
  }) => {
    await simular(page);
    await abrir(page);
    await expect(chips(page)).toHaveCount(8);
    await expect(chips(page)).toHaveText(
      ['Bruna', 'Ifood', 'Mercado', 'Uber', 'Casa', 'Lazer', 'Saúde', 'Pet'].map(
        (n) => new RegExp(n),
      ),
    );
    await expect(busca(page)).toBeVisible();
    // as demais ficam na busca
    await busca(page).fill('viag');
    await expect(page.getByTestId('tag-resultado')).toHaveText(/Viagem/);
  });

  test('SC-001: uma tag frequente custa 1 toque (e vai no corpo da Saída)', async ({ page }) => {
    const envios = await simular(page);
    await abrir(page);
    await valor(page).fill('44,99');
    await descricao(page).fill('Almoço');

    await chip(page, 'Ifood').click(); // o único toque do campo Tags
    await expect(chip(page, 'Ifood')).toHaveAttribute('aria-pressed', 'true');
    await salvar(page).click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios.entradas).toHaveLength(1);
    expect(envios.entradas[0]['tagIds']).toEqual(['t-ifood']);
  });

  test('US1.2: texto sem correspondência cria a tag (comum) e a aplica ao lançamento', async ({
    page,
  }) => {
    const envios = await simular(page);
    await abrir(page);
    await valor(page).fill('30');
    await descricao(page).fill('Remédio');
    await busca(page).fill('Farmácia');
    await page.getByTestId('tag-criar').click();

    await expect(chip(page, 'Farmácia')).toHaveAttribute('aria-pressed', 'true');
    expect(envios.tagsCriadas).toEqual([{ name: 'Farmácia' }]);
    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios.entradas[0]['tagIds']).toEqual(['t-11']);
  });

  test('US1.3: digitar "bruna" usa a tag existente, sem criar outra', async ({ page }) => {
    const envios = await simular(page);
    await abrir(page);
    await busca(page).fill('bruna');
    await expect(page.getByTestId('tag-criar')).toHaveCount(0);
    await busca(page).press('Enter');
    await expect(chip(page, 'Bruna')).toHaveAttribute('aria-pressed', 'true');
    expect(envios.tagsCriadas).toHaveLength(0);
  });

  test('US1.4: segunda tag de acerto é impedida e o aviso sugere Dividir', async ({ page }) => {
    await simular(page);
    await abrir(page);
    await chip(page, 'Bruna').click();
    await busca(page).fill('Carla');
    await page.getByTestId('tag-resultado').click();
    await expect(page.getByTestId('aviso-duas-de-acerto')).toContainText(
      'Só uma tag de acerto por lançamento. Usar Dividir?',
    );
    await expect(chip(page, 'Bruna')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('[data-tag="Carla"][aria-pressed="true"]')).toHaveCount(0);
  });

  test('tags de acerto aparecem com o ícone de pessoa', async ({ page }) => {
    await simular(page);
    await abrir(page);
    await expect(chip(page, 'Bruna').locator('app-icone')).toHaveAttribute('data-icone', 'person');
    await expect(chip(page, 'Ifood').locator('app-icone')).toHaveCount(0);
  });

  test('aba Cartão também tem o campo e envia tagIds na compra', async ({ page }) => {
    const envios = await simular(page);
    await abrir(page);
    await tipo(page, 'Cartão');
    await valor(page).fill('156,81');
    await descricao(page).fill('Ifood');
    await chip(page, 'Bruna').click();
    await chip(page, 'Ifood').click();
    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios.compras[0]['tagIds']).toEqual(['t-bruna', 't-ifood']);
  });

  test('Transferência não tem campo Tags', async ({ page }) => {
    await simular(page);
    await abrir(page);
    await tipo(page, 'Transferência');
    await expect(page.getByTestId('tag-picker')).toHaveCount(0);
  });

  test('sugestão de descrição aplica as tags do último lançamento (FR-003)', async ({ page }) => {
    const envios = await simular(page, {
      sugestoes: {
        ifo: [
          { description: 'Ifood', uses: 4, walletId: 'w-azul', tagIds: ['t-bruna', 't-ifood'] },
        ],
      },
    });
    await abrir(page);
    await descricao(page).fill('Ifo');
    await page.getByTestId('sugestao').click();
    await expect(chip(page, 'Bruna')).toHaveAttribute('aria-pressed', 'true');
    await expect(chip(page, 'Ifood')).toHaveAttribute('aria-pressed', 'true');
    await valor(page).fill('20');
    await salvar(page).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios.entradas[0]['tagIds']).toEqual(['t-bruna', 't-ifood']);
  });

  test('tag escolhida à mão não é trocada pela sugestão', async ({ page }) => {
    await simular(page, {
      sugestoes: { ifo: [{ description: 'Ifood', uses: 4, tagIds: ['t-bruna'] }] },
    });
    await abrir(page);
    await chip(page, 'Mercado').click();
    await descricao(page).fill('Ifo');
    await page.getByTestId('sugestao').click();
    await expect(chip(page, 'Mercado')).toHaveAttribute('aria-pressed', 'true');
    await expect(chip(page, 'Bruna')).toHaveAttribute('aria-pressed', 'false');
  });

  test('nome já existente no servidor (409) usa a tag existente', async ({ page }) => {
    await simular(page);
    await page.route('**/api/v1/tags', (route) =>
      route.request().method() === 'POST'
        ? route.fulfill(
            problema(409, 'tag.name_taken', 'Já existe uma tag com esse nome.', {
              existingTagId: 't-pet',
            }),
          )
        : route.fallback(),
    );
    await abrir(page);
    await busca(page).fill('Pets');
    await page.getByTestId('tag-criar').click();
    await expect(chip(page, 'Pet')).toHaveAttribute('aria-pressed', 'true');
  });
});

// Contra a stack real (E2E_BASE_URL): o servidor valida tags de acerto e nomes (research R1, R2).
test.describe('campo Tags (US1) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  test('US1.1–US1.4: chips por uso, criar, reutilizar "bruna" e a segunda tag de acerto', async ({
    page,
  }) => {
    await comecarEmSaida(page);
    const api = await criarEspacoReal(page);
    const bruna = await api.tag('Bruna', true);
    await api.tag('Carla', true);
    const ifood = await api.tag('Ifood');
    // "Ifood" é mais usada que "Bruna": dois lançamentos recentes (uses90d, ordem das chips)
    const hoje = new Date().toISOString().slice(0, 10);
    await api.entrada('expense', 'Almoço', '20,00', hoje, [ifood.id]);
    await api.entrada('expense', 'Jantar', '30,00', hoje, [ifood.id]);
    await api.entrada('expense', 'Presente', '40,00', hoje, [bruna.id]);

    await page.goto('/');
    await page.getByTestId('lancar').click();
    await expect(chips(page)).toHaveText([/Ifood/, /Bruna/, /Carla/]);

    // US1.2: cria "Farmácia" ali mesmo (não de acerto)
    await valor(page).fill('18,50');
    await descricao(page).fill('Remédio');
    await busca(page).fill('Farmácia');
    await page.getByTestId('tag-criar').click();
    await expect(chip(page, 'Farmácia')).toHaveAttribute('aria-pressed', 'true');

    // US1.3: "bruna" usa a existente
    await busca(page).fill('bruna');
    await expect(page.getByTestId('tag-criar')).toHaveCount(0);
    await busca(page).press('Enter');
    await expect(chip(page, 'Bruna')).toHaveAttribute('aria-pressed', 'true');

    // US1.4: "Carla" também é de acerto
    await chip(page, 'Carla').click();
    await expect(page.getByTestId('aviso-duas-de-acerto')).toBeVisible();
    await expect(chip(page, 'Carla')).toHaveAttribute('aria-pressed', 'false');

    const criacao = page.waitForResponse(
      (r) => r.url().endsWith('/api/v1/entries') && r.request().method() === 'POST',
    );
    await salvar(page).click();
    const entrada = await (await criacao).json();
    expect(entrada.tagIds).toHaveLength(2);
    expect(entrada.settlementTagId).toBe(bruna.id);

    // a tag criada ficou no servidor como tag comum, e só existe uma "Bruna"
    const tags = await (
      await page.request.get('/api/v1/tags?archived=all', { headers: api.auth })
    ).json();
    expect(tags.filter((t: { name: string }) => t.name === 'Bruna')).toHaveLength(1);
    expect(tags.find((t: { name: string }) => t.name === 'Farmácia').isSettlement).toBe(false);
  });
});
