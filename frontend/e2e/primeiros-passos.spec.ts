import { expect, test, type Page } from '@playwright/test';
import { carteiraAzul, sessao } from './tags-api';

// 006 T006 — Guia de primeiros passos (US3.1–US3.4). API simulada: hoje = 10/03/2026 e o estado do
// guia vive na memória do teste. Stack real: cria a conta pela tela e completa o guia de verdade.

const HOJE = '2026-03-10T12:00:00-03:00';
const CHAVES = ['wallets', 'cards', 'settlement_tags', 'installments', 'recurrences'] as const;

interface Estado {
  steps: Record<string, string>;
  dismissed: boolean;
  autoStart: boolean;
}

interface Simulacao {
  estado: Estado;
  puts: { step: string; state: string }[];
  carteirasCriadas: Record<string, string>[];
  dispensou: () => boolean;
}

async function simular(page: Page, inicial: Partial<Estado> = {}): Promise<Simulacao> {
  const estado: Estado = { steps: {}, dismissed: false, autoStart: true, ...inicial };
  const sim: Simulacao = {
    estado,
    puts: [],
    carteirasCriadas: [],
    dispensou: () => estado.dismissed,
  };

  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/onboarding', (route) => route.fulfill({ json: estado }));
  await page.route('**/api/v1/onboarding/steps/*', (route) => {
    const step = route.request().url().split('/').pop()!;
    const { state } = route.request().postDataJSON();
    sim.puts.push({ step, state });
    estado.steps[step] = state;
    return route.fulfill({ json: estado });
  });
  await page.route('**/api/v1/onboarding/dismiss', (route) => {
    estado.dismissed = true;
    estado.autoStart = false;
    return route.fulfill({ json: estado });
  });
  await page.route('**/api/v1/wallets**', (route) => {
    if (route.request().method() === 'POST') {
      const corpo = route.request().postDataJSON();
      sim.carteirasCriadas.push(corpo);
      estado.autoStart = false;
      return route.fulfill({ status: 201, json: { ...carteiraAzul, name: corpo.name } });
    }
    return route.fulfill({ json: sim.carteirasCriadas.length > 0 ? [carteiraAzul] : [] });
  });
  await page.route('**/api/v1/credit-cards**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/tags**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/months/**', (route) =>
    route.fulfill({ status: 500, json: { status: 500, code: 'simulado', detail: 'sem mês' } }),
  );
  await page.route('**/api/v1/now', (route) =>
    route.fulfill({
      json: {
        today: '2026-03-10',
        wallets: [],
        archivedWalletsBalanceCents: 0,
        totalBalanceCents: 0,
        invoices: [],
        pending: { count: 0, toReceiveCents: 0, toPayCents: 0 },
        settlements: [],
      },
    }),
  );
  await page.route('**/api/v1/years/*/panel', (route) =>
    route.fulfill({ status: 500, json: { status: 500, code: 'simulado', detail: 'sem painel' } }),
  );
  return sim;
}

test.describe('primeiros passos (US3) — API simulada', () => {
  test('US3.1: Espaço novo abre o guia na primeira etapa, com as 5 etapas', async ({ page }) => {
    await simular(page);
    await page.goto('/');
    await expect(page).toHaveURL(/\/primeiros-passos\/carteiras$/);
    await expect(page.getByTestId('progresso')).toHaveText('Etapa 1 de 5');
    const trilha = page.getByTestId('etapa-trilha');
    await expect(trilha).toHaveText([
      'Carteiras',
      'Cartões',
      'Pessoas que te devolvem dinheiro',
      'Parcelamentos em andamento',
      'Contas fixas e salário',
    ]);
  });

  test('US3.2: Pular percorre as etapas e, ao final, cai no mês de hoje', async ({ page }) => {
    const sim = await simular(page);
    await page.goto('/primeiros-passos/carteiras');
    const rotas = ['cartoes', 'pessoas', 'parcelamentos', 'contas-fixas'];
    for (const rota of rotas) {
      await page.getByTestId('pular').click();
      await expect(page).toHaveURL(new RegExp(`/primeiros-passos/${rota}$`));
    }
    await expect(page.getByTestId('continuar')).toHaveText('Concluir');
    await page.getByTestId('pular').click();
    await expect(page).toHaveURL(/\/mes\/2026-03$/);
    expect(sim.puts).toEqual(CHAVES.map((step) => ({ step, state: 'skipped' })));
  });

  test('US3.3: sair no meio e voltar pelo Painel retoma na primeira etapa pendente', async ({
    page,
  }) => {
    await simular(page, {
      steps: { wallets: 'done', cards: 'skipped' },
      autoStart: false,
    });
    await page.goto('/painel/2026');
    const cartao = page.getByTestId('continuar-configuracao');
    await expect(cartao).toContainText('Continuar configuração');
    await cartao.click();
    await expect(page).toHaveURL(/\/primeiros-passos\/pessoas$/);
    await expect(page.getByTestId('etapa-titulo')).toHaveText('Pessoas que te devolvem dinheiro');
  });

  test('US3.3: o guia não reaparece sozinho depois de dispensado; o cartão some', async ({
    page,
  }) => {
    const sim = await simular(page);
    await page.goto('/');
    await expect(page).toHaveURL(/\/primeiros-passos\/carteiras$/);
    await page.getByTestId('dispensar').click();
    await expect(page).toHaveURL(/\/mes\/2026-03$/);
    expect(sim.dispensou()).toBe(true);

    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/2026-03$/);
    await page.goto('/painel/2026');
    await expect(page.getByTestId('agora')).toBeVisible();
    await expect(page.getByTestId('continuar-configuracao')).toHaveCount(0);
  });

  test('US3.3: tudo concluído ou pulado também esconde o cartão', async ({ page }) => {
    await simular(page, {
      steps: Object.fromEntries(CHAVES.map((c, i) => [c, i % 2 ? 'skipped' : 'done'])),
      autoStart: false,
    });
    await page.goto('/painel/2026');
    await expect(page.getByTestId('agora')).toBeVisible();
    await expect(page.getByTestId('continuar-configuracao')).toHaveCount(0);
  });

  test('US3.4: a carteira sugere a data de hoje e explica o saldo; Continuar avança', async ({
    page,
  }) => {
    const sim = await simular(page);
    await page.goto('/primeiros-passos/carteiras');
    await expect(page.getByTestId('etapa-explicacao')).toContainText(
      'informe o saldo que está na conta hoje',
    );
    await expect(page.getByLabel('Data do saldo inicial')).toHaveValue('2026-03-10');

    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Saldo inicial', { exact: true }).fill('1800,00');
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page.getByTestId('etapa-cadastrados')).toContainText('1 item cadastrado');
    await expect(page).toHaveURL(/\/primeiros-passos\/carteiras$/);
    expect(sim.carteirasCriadas).toHaveLength(1);
    expect(sim.carteirasCriadas[0]).toMatchObject({
      name: 'Azul',
      initialBalance: '1800,00',
      initialBalanceDate: '2026-03-10',
    });

    await page.getByTestId('continuar').click();
    await expect(page).toHaveURL(/\/primeiros-passos\/cartoes$/);
    expect(sim.puts).toEqual([{ step: 'wallets', state: 'done' }]);
  });

  test('sem rolagem horizontal no celular e no computador', async ({ page }) => {
    await simular(page);
    await page.goto('/primeiros-passos/contas-fixas');
    await expect(page.getByTestId('etapa-titulo')).toHaveText('Contas fixas e salário');
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
    );
    expect(overflow).toBe(false);
  });
});

// Contra a stack real (E2E_BASE_URL); sem ela, é pulado.
test.describe('primeiros passos (US3) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  test('conta nova: completa o guia e o mês já mostra saldo, fatura e tag de acerto', async ({
    page,
  }) => {
    const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
    await page.goto('/criar-conta');
    await page.getByLabel('Nome', { exact: true }).fill('Lia Souza');
    await page.getByLabel('E-mail').fill(email);
    await page.getByLabel('Senha', { exact: true }).fill('senha-teste-123');
    await page.getByRole('button', { name: 'Criar conta' }).click();

    // Etapa 1: carteira Azul com saldo
    await expect(page).toHaveURL(/\/primeiros-passos\/carteiras$/);
    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Saldo inicial', { exact: true }).fill('1800,00');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByTestId('etapa-cadastrados')).toBeVisible();
    await page.getByTestId('continuar').click();

    // Etapa 2: cartão Verde
    await expect(page).toHaveURL(/\/primeiros-passos\/cartoes$/);
    await page.getByLabel('Nome', { exact: true }).fill('Verde');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByTestId('etapa-cadastrados')).toBeVisible();
    await page.getByTestId('continuar').click();

    // Etapa 3: Bruna (tag de acerto)
    await expect(page).toHaveURL(/\/primeiros-passos\/pessoas$/);
    await page.getByLabel('Nome da pessoa').fill('Bruna');
    await page.getByRole('button', { name: 'Adicionar pessoa' }).click();
    await expect(page.getByTestId('pessoas-cadastradas')).toContainText('Bruna');
    await page.getByTestId('continuar').click();

    // Etapas 4 e 5: pular
    await expect(page).toHaveURL(/\/primeiros-passos\/parcelamentos$/);
    await page.getByTestId('pular').click();
    await expect(page).toHaveURL(/\/primeiros-passos\/contas-fixas$/);
    await page.getByTestId('pular').click();

    // Ao final, o mês de hoje
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    await expect(page.getByTestId('resumo')).toContainText('1.800,00');

    // O guia não reaparece sozinho (já tem carteira) e o Painel não oferece "Continuar configuração".
    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    await page.goto('/painel');
    await expect(page.getByTestId('agora-carteira')).toContainText('Azul');
    await expect(page.getByTestId('continuar-configuracao')).toHaveCount(0);
  });

  test('conta nova: dispensar o guia leva ao mês e ele não volta', async ({ page }) => {
    const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
    await page.goto('/criar-conta');
    await page.getByLabel('Nome', { exact: true }).fill('Lia Souza');
    await page.getByLabel('E-mail').fill(email);
    await page.getByLabel('Senha', { exact: true }).fill('senha-teste-123');
    await page.getByRole('button', { name: 'Criar conta' }).click();
    await expect(page).toHaveURL(/\/primeiros-passos\/carteiras$/);
    await page.getByTestId('dispensar').click();
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
  });
});
