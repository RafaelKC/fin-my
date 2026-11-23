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
  date: string,
  status: Lanc['status'] = 'planned',
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

/** Fevereiro tem "Carla atrasado" (a receber) e "Léo Casa" (a pagar), ambos previstos. */
const cenario = () => [
  lanc('1', 'income', 'Carla atrasado', 30000, '2026-02-10'),
  lanc('2', 'expense', 'Léo Casa', 12050, '2026-02-15'),
  lanc('3', 'income', 'Salário', 150000, '2026-03-05', 'settled'),
];

const vazia = {
  openingBalanceCents: 0,
  walletsInitialBalanceCents: 0,
  incomeCents: 0,
  expenseCents: 0,
  resultCents: 0,
  transfersInCents: 0,
  transfersOutCents: 0,
  finalBalanceCents: 0,
};

/** API simulada com estado: pendência = prevista com mês de referência anterior a março. */
async function simular(page: Page, lista: Lanc[] = cenario()) {
  const excluidas = new Map<string, Lanc[]>();
  const pedidos: string[] = [];
  const pendentes = () =>
    lista.filter((e) => e.status === 'planned' && e.referenceMonth < '2026-03');

  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (r) => r.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (r) => r.fulfill({ json: [azul] }));
  await page.route('**/api/v1/months/*', (r) => {
    const mes = new URL(r.request().url()).pathname.split('/').pop()!;
    const p = pendentes();
    return r.fulfill({
      json: {
        month: mes,
        today: '2026-03-10',
        walletIds: null,
        planned: vazia,
        actual: vazia,
        entries: lista.filter((e) => e.referenceMonth === mes),
        transfers: [],
        pending: {
          count: p.length,
          toReceiveCents: p
            .filter((e) => e.kind === 'income')
            .reduce((a, e) => a + e.amountCents, 0),
          toPayCents: p.filter((e) => e.kind === 'expense').reduce((a, e) => a + e.amountCents, 0),
        },
      },
    });
  });
  await page.route('**/api/v1/entries/pending', (r) => r.fulfill({ json: pendentes() }));
  await page.route('**/api/v1/entries/bulk', (r) => {
    const { ids, action } = r.request().postDataJSON() as { ids: string[]; action: string };
    pedidos.push(`${action} ${ids.join(',')}`);
    const alvo = lista.filter((e) => ids.includes(e.id));
    let lote: string | null = null;
    for (const e of alvo) {
      if (action === 'settle') e.status = 'settled';
      if (action === 'move_to_current_month') e.referenceMonth = '2026-03';
    }
    if (action === 'delete') {
      lote = 'lote-1';
      excluidas.set(lote, alvo);
      for (const e of alvo) lista.splice(lista.indexOf(e), 1);
    }
    return r.fulfill({ json: { affected: alvo.length, deletionBatchId: lote } });
  });
  await page.route('**/api/v1/deletions/*/restore', (r) => {
    const lote = r.request().url().split('/').slice(-2)[0];
    pedidos.push(`restore ${lote}`);
    lista.push(...(excluidas.get(lote) ?? []));
    return r.fulfill({ status: 204 });
  });
  return pedidos;
}

const aviso = (page: Page) => page.getByTestId('pendencias-aviso');
const abrir = (page: Page) => page.getByTestId('pendencias-alternar').click();
const selecionar = (page: Page, descricao: string) =>
  page.getByRole('checkbox', { name: `Selecionar ${descricao}` }).check();

test.describe('pendências (US4) — API simulada', () => {
  test('US4.1: março mostra a contagem e os totais; outro mês não mostra o aviso', async ({
    page,
  }) => {
    await simular(page);
    await page.goto('/mes/2026-03');
    await expect(aviso(page)).toContainText('2 pendências de meses anteriores');
    await expect(page.getByTestId('pendencias-receber')).toHaveText('R$ 300,00');
    await expect(page.getByTestId('pendencias-pagar')).toHaveText('R$ 120,50');

    await page.goto('/mes/2026-04');
    await expect(page.getByTestId('resumo')).toBeVisible();
    await expect(page.getByTestId('pendencias')).toHaveCount(0);
  });

  test('US4.2: "Trazer para este mês" move só o mês de referência e a pendência some', async ({
    page,
  }) => {
    const pedidos = await simular(page);
    await page.goto('/mes/2026-03');
    await abrir(page);
    await selecionar(page, 'Carla atrasado');
    await page.getByTestId('pendencias-trazer').click();

    await expect(aviso(page)).toContainText('1 pendência de meses anteriores');
    await expect(page.getByTestId('pendencias-receber')).toHaveText('R$ 0,00');
    await expect(
      page.getByTestId('lancamento').filter({ hasText: 'Carla atrasado' }),
    ).toBeVisible();
    expect(pedidos).toContain('move_to_current_month 1');
  });

  test('US4.3: "Marcar como recebida" realiza várias de uma vez', async ({ page }) => {
    const pedidos = await simular(page);
    await page.goto('/mes/2026-03');
    await abrir(page);
    await page.getByTestId('pendencias-todas').check();
    await expect(page.getByTestId('pendencias-realizar')).toHaveText('Marcar como recebida/paga');
    await page.getByTestId('pendencias-realizar').click();

    await expect(page.getByTestId('pendencias')).toHaveCount(0);
    expect(pedidos).toContain('settle 1,2');
  });

  test('US4.3: com só uma entrada selecionada o botão diz "recebida"', async ({ page }) => {
    await simular(page);
    await page.goto('/mes/2026-03');
    await abrir(page);
    await selecionar(page, 'Carla atrasado');
    await expect(page.getByTestId('pendencias-realizar')).toHaveText('Marcar como recebida');
    await page.getByRole('checkbox', { name: 'Selecionar Carla atrasado' }).uncheck();
    await selecionar(page, 'Léo Casa');
    await expect(page.getByTestId('pendencias-realizar')).toHaveText('Marcar como paga');
  });

  test('US4.4: excluir oferece Desfazer e desfazer traz a pendência de volta', async ({ page }) => {
    const pedidos = await simular(page);
    await page.goto('/mes/2026-03');
    await abrir(page);
    await selecionar(page, 'Carla atrasado');
    await page.getByTestId('pendencias-excluir').click();

    await expect(aviso(page)).toContainText('1 pendência de meses anteriores');
    const desfazer = page.getByRole('button', { name: 'Desfazer' });
    await expect(desfazer).toBeVisible();
    await desfazer.click();

    await expect(aviso(page)).toContainText('2 pendências de meses anteriores');
    await expect(page.getByTestId('pendencias-receber')).toHaveText('R$ 300,00');
    expect(pedidos).toEqual(['delete 1', 'restore lote-1']);
  });

  test('sem seleção as ações ficam desabilitadas', async ({ page }) => {
    await simular(page);
    await page.goto('/mes/2026-03');
    await abrir(page);
    for (const id of ['pendencias-realizar', 'pendencias-trazer', 'pendencias-excluir']) {
      await expect(page.getByTestId(id)).toBeDisabled();
    }
  });
});
