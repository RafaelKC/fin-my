import { expect, test, type Page, type Route } from '@playwright/test';
import { irPara } from './navegacao';
import {
  HOJE,
  carteiraAzul,
  criarEspacoReal,
  problema,
  sessao,
  tag,
  type TagSimulada,
} from './tags-api';

// 004 T005 — tela /tags (US5.1–US5.4). A API simulada confere o que o app envia e como mostra os
// erros; a stack real confere as regras no servidor (junção, conflito de acerto, exclusão).

const conflitos = [
  { type: 'expense', id: 'e1', description: 'Jantar com Léo', date: '2026-03-05' },
  { type: 'card_purchase', id: 'p1', description: 'Mercado do mês', date: '2026-03-07' },
];

const linha = (page: Page, nome: string) =>
  page.locator(`[data-testid="tag"][data-nome="${nome}"]`);

async function simular(page: Page, tags: TagSimulada[]) {
  const chamadas: { metodo: string; caminho: string; corpo: unknown }[] = [];
  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteiraAzul] }));
  await page.route('**/api/v1/credit-cards**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/tags**', async (route: Route) => {
    const req = route.request();
    const caminho = new URL(req.url()).pathname.replace('/api/v1', '');
    if (req.method() === 'GET') return route.fulfill({ json: tags });
    chamadas.push({ metodo: req.method(), caminho, corpo: req.postDataJSON() });
    if (caminho.endsWith('/merge') && caminho.includes('t-carla')) {
      return route.fulfill(
        problema(409, 'tag.settlement_conflict', 'Alguns lançamentos ficariam com duas tags.', {
          conflicts: conflitos,
        }),
      );
    }
    if (req.method() === 'PUT' && caminho.includes('t-carla')) {
      return route.fulfill(
        problema(409, 'tag.settlement_conflict', 'Alguns lançamentos ficariam com duas tags.', {
          conflicts: conflitos,
        }),
      );
    }
    return route.fulfill({ json: tags[0] });
  });
  return chamadas;
}

test.describe('Gerenciar tags (API simulada)', () => {
  const mercado = tag('t-mercado', 'Mercado', 12);
  const supermercado = tag('t-super', 'Supermercado', 3);
  const bruna = tag('t-bruna', 'Bruna', 30, true);
  const carla = tag('t-carla', 'Carla', 4);
  const sobra = tag('t-sobra', 'Sobra', 0);

  test('US5.1: junta "Supermercado" em "Mercado" depois de confirmar', async ({ page }) => {
    const chamadas = await simular(page, [mercado, supermercado, sobra]);
    await page.goto('/mes/2026-03');
    await expect(page.getByRole('heading', { name: 'Março de 2026' })).toBeVisible();
    await irPara(page, 'Tags');
    await expect(page).toHaveURL(/\/tags$/);
    await expect(page.getByTestId('tag')).toHaveCount(3);

    const l = linha(page, 'Supermercado');
    await l.getByTestId('juntar').click();
    await l.getByTestId('campo-destino').selectOption({ label: 'Mercado' });
    await l.getByTestId('continuar-juncao').click();
    await expect(l.getByTestId('confirmacao-juncao')).toContainText('“Mercado”');
    await l.getByTestId('confirmar-juncao').click();

    await expect.poll(() => chamadas.length).toBe(1);
    expect(chamadas[0]).toEqual({
      metodo: 'POST',
      caminho: '/tags/t-super/merge',
      corpo: { targetTagId: 't-mercado' },
    });
  });

  test('US5.2: marcar "Carla" como de acerto com conflito lista os lançamentos', async ({
    page,
  }) => {
    const chamadas = await simular(page, [bruna, carla]);
    await page.goto('/tags');
    const l = linha(page, 'Carla');
    await l.getByTestId('editar').click();
    await l.getByTestId('campo-acerto').check();
    await l.getByTestId('salvar').click();

    await expect(l.getByTestId('erro')).toContainText('duas tags');
    await expect(l.getByTestId('conflito')).toHaveText([/Jantar com Léo/, /Mercado do mês/]);
    expect(chamadas[0].corpo).toMatchObject({ name: 'Carla', isSettlement: true, version: 1 });
  });

  test('US5.3: tag em uso só pode ser arquivada; sem uso pode ser excluída', async ({ page }) => {
    const chamadas = await simular(page, [mercado, sobra]);
    await page.goto('/tags');
    await expect(linha(page, 'Mercado').getByTestId('excluir')).toHaveCount(0);
    await expect(linha(page, 'Mercado').getByTestId('arquivar')).toBeVisible();

    const l = linha(page, 'Sobra');
    await l.getByTestId('excluir').click();
    await l.getByTestId('confirmar-exclusao').click();
    await expect.poll(() => chamadas.length).toBe(1);
    expect(chamadas[0]).toMatchObject({ metodo: 'DELETE', caminho: '/tags/t-sobra' });
  });

  test('US5.4: junção que deixaria duas tags de acerto lista os lançamentos', async ({ page }) => {
    await simular(page, [bruna, carla]);
    await page.goto('/tags');
    const l = linha(page, 'Carla');
    await l.getByTestId('juntar').click();
    await l.getByTestId('campo-destino').selectOption({ label: 'Bruna' });
    await l.getByTestId('continuar-juncao').click();
    await l.getByTestId('confirmar-juncao').click();
    await expect(l.getByTestId('conflito')).toHaveCount(2);
  });
});

test.describe('Gerenciar tags (stack real)', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  test('US5.1–US5.4 com o servidor', async ({ page }) => {
    const api = await criarEspacoReal(page);
    const bruna = await api.tag('Bruna', true);
    const carla = await api.tag('Carla');
    const mercado = await api.tag('Mercado');
    const supermercado = await api.tag('Supermercado');
    await api.tag('Sobra');
    const duda = await api.tag('Duda', true);
    const hoje = new Date().toISOString().slice(0, 10);
    await api.entrada('expense', 'Feira', '30,00', hoje, [supermercado.id]);
    await api.entrada('expense', 'Compras', '50,00', hoje, [mercado.id, supermercado.id]);
    await api.entrada('expense', 'Jantar com Léo', '80,00', hoje, [bruna.id, carla.id]);

    await api.entrada('expense', 'Presente', '60,00', hoje, [carla.id, duda.id]);

    await page.goto('/tags');
    await expect(page.getByTestId('tag')).toHaveCount(6);

    // US5.2: Carla + Bruna juntas num lançamento → não pode virar de acerto
    const lc = linha(page, 'Carla');
    await lc.getByTestId('editar').click();
    await lc.getByTestId('campo-acerto').check();
    await lc.getByTestId('salvar').click();
    // "Jantar com Léo" (Bruna + Carla) e "Presente" (Carla + Duda) ficariam com duas de acerto
    await expect(lc.getByTestId('conflito')).toHaveCount(2);
    await expect(lc.getByTestId('conflito').filter({ hasText: 'Jantar com Léo' })).toHaveCount(1);
    await lc.getByRole('button', { name: 'Cancelar' }).click();

    // US5.4: Carla em Bruna deixaria "Presente" com Bruna e Duda
    await lc.getByTestId('juntar').click();
    await lc.getByTestId('campo-destino').selectOption({ label: 'Bruna' });
    await lc.getByTestId('continuar-juncao').click();
    await lc.getByTestId('confirmar-juncao').click();
    await expect(lc.getByTestId('conflito')).toHaveText([/Presente/]);
    await lc.getByRole('button', { name: 'Cancelar' }).click();

    // US5.3: em uso → sem "Excluir"; sem uso → exclui
    await expect(linha(page, 'Mercado').getByTestId('excluir')).toHaveCount(0);
    const ls = linha(page, 'Sobra');
    await ls.getByTestId('excluir').click();
    await ls.getByTestId('confirmar-exclusao').click();
    await expect(linha(page, 'Sobra')).toHaveCount(0);

    // US5.1: junta Supermercado em Mercado
    const l = linha(page, 'Supermercado');
    await l.getByTestId('juntar').click();
    await l.getByTestId('campo-destino').selectOption({ label: 'Mercado' });
    await l.getByTestId('continuar-juncao').click();
    await l.getByTestId('confirmar-juncao').click();
    await expect(linha(page, 'Supermercado')).toHaveCount(0);
    await expect(linha(page, 'Mercado').getByTestId('tag-usos')).toContainText('2 usos');

    // arquivar e ver em "Arquivadas"
    await linha(page, 'Mercado').getByTestId('arquivar').click();
    await expect(linha(page, 'Mercado')).toHaveCount(0);
    await page.getByTestId('filtro-arquivadas').check();
    await expect(linha(page, 'Mercado').getByTestId('arquivar')).toContainText('Desarquivar');
  });
});
