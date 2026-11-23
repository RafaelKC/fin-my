import { expect, test, type Page } from '@playwright/test';
import { comecarEmSaida } from './navegacao';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};

/** Cenário da US5: hoje é 29/06/2026; Azul com 1.000,00 e Poupança com 0,00. */
const HOJE = '2026-06-29T12:00:00-03:00';

const base = {
  type: 'checking',
  initialBalanceDate: '2026-06-01',
  archived: false,
  hasEntries: false,
  version: 1,
};
const azul = {
  ...base,
  id: 'w-azul',
  name: 'Azul',
  color: '#1E88E5',
  isPrimary: true,
  initialBalanceCents: 100000,
};
const poupanca = {
  ...base,
  id: 'w-poupanca',
  name: 'Poupança',
  color: '#43A047',
  isPrimary: false,
  initialBalanceCents: 0,
};

interface Transf {
  id: string;
  fromWalletId: string;
  toWalletId: string;
  amountCents: number;
  date: string;
  description: string;
  createdAt: string;
  version: number;
}
interface Envio {
  corpo: Record<string, string>;
  chave: string | undefined;
}

/** API simulada com estado: carteiras com saldo, transferências e a visão do mês de junho. */
async function simular(page: Page) {
  const transferencias: Transf[] = [];
  const envios: Envio[] = [];
  const carteiras = [azul, poupanca];

  const saldo = (w: (typeof carteiras)[number]) =>
    w.initialBalanceCents +
    transferencias.filter((t) => t.toWalletId === w.id).reduce((a, t) => a + t.amountCents, 0) -
    transferencias.filter((t) => t.fromWalletId === w.id).reduce((a, t) => a + t.amountCents, 0);

  await comecarEmSaida(page);
  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (r) => r.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (r) =>
    r.fulfill({ json: carteiras.map((w) => ({ ...w, currentBalanceCents: saldo(w) })) }),
  );
  await page.route('**/api/v1/transfers', (r) => {
    const corpo = r.request().postDataJSON();
    envios.push({ corpo, chave: r.request().headers()['idempotency-key'] });
    const t: Transf = {
      id: `t${envios.length}`,
      fromWalletId: corpo.fromWalletId,
      toWalletId: corpo.toWalletId,
      amountCents: Math.round(Number(String(corpo.amount).replace(',', '.')) * 100),
      date: corpo.date,
      description: corpo.description || 'Transferência',
      createdAt: '2026-06-29T15:00:00Z',
      version: 1,
    };
    transferencias.push(t);
    return r.fulfill({ status: 201, json: t });
  });
  await page.route('**/api/v1/months/*', (r) => {
    const url = new URL(r.request().url());
    const mes = url.pathname.split('/').pop()!;
    const filtro = url.searchParams.get('walletIds')?.split(',') ?? null;
    const selecionadas = carteiras.filter((w) => !filtro || filtro.includes(w.id));
    const abertura = selecionadas.reduce((a, w) => a + w.initialBalanceCents, 0);
    const doMes = transferencias.filter(
      (t) =>
        t.date.startsWith(mes) &&
        (!filtro || filtro.includes(t.fromWalletId) || filtro.includes(t.toWalletId)),
    );
    const dentro = (id: string) => !filtro || filtro.includes(id);
    // com todas as carteiras as transferências se anulam (RT-08)
    const recebidas = filtro
      ? doMes.filter((t) => dentro(t.toWalletId) && !dentro(t.fromWalletId))
      : [];
    const enviadas = filtro
      ? doMes.filter((t) => dentro(t.fromWalletId) && !dentro(t.toWalletId))
      : [];
    const soma = (l: Transf[]) => l.reduce((a, t) => a + t.amountCents, 0);
    const coluna = {
      openingBalanceCents: abertura,
      walletsInitialBalanceCents: abertura,
      incomeCents: 0,
      expenseCents: 0,
      resultCents: 0,
      transfersInCents: soma(recebidas),
      transfersOutCents: soma(enviadas),
      finalBalanceCents: abertura + soma(recebidas) - soma(enviadas),
    };
    return r.fulfill({
      json: {
        month: mes,
        today: '2026-06-29',
        walletIds: filtro,
        planned: coluna,
        actual: coluna,
        entries: [],
        transfers: doMes,
        pending: { count: 0, toReceiveCents: 0, toPayCents: 0 },
      },
    });
  });
  return { envios, transferencias };
}

const celula = (page: Page, linha: string, col: 'previsto' | 'realizado') =>
  page.getByTestId(`resumo-${linha}`).getByTestId(col);

async function abrirTransferencia(page: Page) {
  await page.getByTestId('lancar').click();
  await page.getByRole('radio', { name: 'Transferência', exact: true }).click();
}

test.describe('transferências (US5) — API simulada', () => {
  test('formulário: De/Para, descrição padrão, sem situação nem sugestões', async ({ page }) => {
    await simular(page);
    await page.goto('/mes/2026-06');
    await abrirTransferencia(page);

    await expect(page.getByLabel('De', { exact: true })).toHaveValue('w-azul');
    await expect(page.getByLabel('Para', { exact: true })).toHaveValue('w-poupanca');
    await expect(page.getByRole('textbox', { name: 'Descrição' })).toHaveAttribute(
      'placeholder',
      'Transferência',
    );
    await expect(page.getByText('Situação', { exact: true })).toHaveCount(0);
    await expect(page.getByTestId('sugestao')).toHaveCount(0);
  });

  test('US5.1: transferir 20,00 de Azul para Poupança move os saldos e não muda os totais do mês', async ({
    page,
  }) => {
    const { envios } = await simular(page);
    await page.goto('/mes/2026-06');
    await expect(celula(page, 'resultado', 'realizado')).toHaveText('R$ 0,00');

    await abrirTransferencia(page);
    await page.getByLabel('Valor', { exact: true }).fill('20');
    await page.getByLabel('Data').fill('2026-06-29');
    await page.getByTestId('salvar').click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios).toHaveLength(1);
    expect(envios[0].corpo).toEqual({
      fromWalletId: 'w-azul',
      toWalletId: 'w-poupanca',
      amount: '20',
      date: '2026-06-29',
    });
    expect(envios[0].chave).toMatch(/^[0-9a-f-]{36}$/);

    // visão de todas as carteiras: entradas, saídas e resultado não mudam (RT-08)
    await expect(page.getByTestId('transferencias-alternar')).toContainText('Transferências (1)');
    await expect(celula(page, 'entradas', 'realizado')).toHaveText('R$ 0,00');
    await expect(celula(page, 'saidas', 'realizado')).toHaveText('R$ 0,00');
    await expect(celula(page, 'resultado', 'realizado')).toHaveText('R$ 0,00');
    await expect(celula(page, 'saldo-final', 'realizado')).toHaveText('R$ 1.000,00');

    // saldos das carteiras: Azul 980,00 e Poupança 20,00
    await page.goto('/carteiras');
    const azulLinha = page.getByTestId('carteira').filter({ hasText: 'Azul' });
    const poupancaLinha = page.getByTestId('carteira').filter({ hasText: 'Poupança' });
    await expect(azulLinha).toContainText('R$ 980,00');
    await expect(poupancaLinha).toContainText('R$ 20,00');
  });

  test('US5.2: filtrada pela Azul, a transferência aparece como "Transferência enviada" de 20,00', async ({
    page,
  }) => {
    await simular(page);
    await page.goto('/mes/2026-06');
    await abrirTransferencia(page);
    await page.getByLabel('Valor', { exact: true }).fill('20');
    await page.getByLabel('Data').fill('2026-06-29');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await page.getByTestId('filtro-carteira').filter({ hasText: 'Azul' }).click();
    await expect(page).toHaveURL(/carteiras=w-azul/);
    await expect(celula(page, 'transferencias-enviadas', 'realizado')).toHaveText('R$ 20,00');
    await expect(celula(page, 'saldo-final', 'realizado')).toHaveText('R$ 980,00');

    // lista recolhida por padrão; abre ao tocar
    await expect(page.getByTestId('transferencia')).toHaveCount(0);
    await page.getByTestId('transferencias-alternar').click();
    await expect(page.getByTestId('transferencia-rotulo')).toHaveText('Transferência enviada');
    await expect(page.getByTestId('transferencia')).toContainText('R$ 20,00');

    // pela Poupança, a mesma transferência é "recebida"
    await page.getByTestId('filtro-carteira').filter({ hasText: 'Azul' }).click();
    await page.getByTestId('filtro-carteira').filter({ hasText: 'Poupança' }).click();
    await expect(page.getByTestId('transferencia-rotulo')).toHaveText('Transferência recebida');
    await expect(celula(page, 'saldo-final', 'realizado')).toHaveText('R$ 20,00');
  });

  test('US5.3: origem e destino iguais são barrados no formulário', async ({ page }) => {
    const { envios } = await simular(page);
    await page.goto('/mes/2026-06');
    await abrirTransferencia(page);
    await page.getByLabel('Valor', { exact: true }).fill('20');
    await page.getByLabel('Para', { exact: true }).selectOption('w-azul');
    await page.getByTestId('salvar').click();

    await expect(page.getByTestId('erro-destino')).toHaveText('Escolha carteiras diferentes.');
    await expect(page.getByRole('dialog')).toBeVisible();
    expect(envios).toHaveLength(0);
  });
});
