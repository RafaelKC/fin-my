import { expect, test, type Page } from '@playwright/test';
import { irPara } from './navegacao';
import { carteiraAzul, criarEspacoReal, problema, sessao } from './tags-api';

/** Recorrência como a API simulada a guarda (só o que as telas usam). */
interface Recorrencia {
  id: string;
  kind: 'income' | 'expense' | 'card_purchase';
  description: string;
  amountCents: number;
  dayOfMonth: number;
  walletId: string | null;
  creditCardId: string | null;
  tagIds: string[];
  startMonth: string;
  endType: 'none' | 'until_month' | 'after_count';
  endMonth: string | null;
  occurrenceLimit: number | null;
  referenceMonthRule: 'same_month' | 'next_month' | null;
  generatedUntilMonth: string | null;
  status: 'active' | 'ended';
  nextOccurrenceDate: string | null;
  needsReview: boolean;
  reviewReason: string | null;
  version: number;
}

const centavos = (texto: string) => Math.round(Number(texto.replace(',', '.')) * 100);

function mesAnterior(mes: string): string {
  const [ano, m] = mes.split('-').map(Number);
  return m === 1 ? `${ano - 1}-12` : `${ano}-${String(m - 1).padStart(2, '0')}`;
}

/** `AAAA-MM` daqui a `n` meses a partir do mês de hoje. */
function mesDaquiA(n: number): string {
  const d = new Date();
  const total = d.getFullYear() * 12 + d.getMonth() + n;
  return `${Math.floor(total / 12)}-${String((total % 12) + 1).padStart(2, '0')}`;
}

/** API de recorrências em memória (regras do contrato: alterar a partir de, encerrar, excluir). */
async function simularApi(page: Page) {
  let seq = 0;
  const lista: Recorrencia[] = [];

  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteiraAzul] }));
  await page.route('**/api/v1/credit-cards**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/tags**', (route) => route.fulfill({ json: [] }));
  await page.route('**/api/v1/recurrences**', async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const [id, acao] = url.pathname.replace('/api/v1/recurrences', '').split('/').filter(Boolean);
    const corpo = req.postDataJSON() ?? {};
    const alvo = lista.find((r) => r.id === id);

    if (!id) {
      if (req.method() === 'GET') {
        const situacao = url.searchParams.get('status');
        const itens = lista.filter((r) => situacao === 'all' || r.status === situacao);
        const ativas = lista.filter((r) => r.status === 'active');
        const soma = (tipo: (r: Recorrencia) => boolean) =>
          ativas.filter(tipo).reduce((total, r) => total + r.amountCents, 0);
        return route.fulfill({
          json: {
            items: itens,
            monthlyIncomeCents: soma((r) => r.kind === 'income'),
            monthlyExpenseCents: soma((r) => r.kind !== 'income'),
          },
        });
      }
      const nova: Recorrencia = {
        ...corpo,
        id: `r${++seq}`,
        amountCents: centavos(corpo.amount),
        generatedUntilMonth: '2027-12',
        status: 'active',
        nextOccurrenceDate: `${corpo.startMonth}-${String(corpo.dayOfMonth).padStart(2, '0')}`,
        needsReview: false,
        reviewReason: null,
        version: 1,
      };
      lista.push(nova);
      return route.fulfill({ status: 201, json: nova });
    }
    if (!alvo) return route.fulfill(problema(404, 'not_found', 'Não encontrada.'));

    if (!acao && req.method() === 'GET') return route.fulfill({ json: alvo });
    if (!acao && req.method() === 'PUT') {
      if (corpo.version !== alvo.version)
        return route.fulfill(
          problema(409, 'concurrency_conflict', 'Esta recorrência foi alterada em outro lugar.'),
        );
      Object.assign(alvo, corpo, {
        amountCents: centavos(corpo.amount),
        version: alvo.version + 1,
      });
      return route.fulfill({ json: { ...alvo, affectedCount: 4 } });
    }
    if (acao === 'end') {
      if (alvo.status === 'ended')
        return route.fulfill(problema(422, 'recurrence.already_ended', 'Já está encerrada.'));
      Object.assign(alvo, {
        status: 'ended',
        endType: 'until_month',
        endMonth: mesAnterior(corpo.fromMonth),
        nextOccurrenceDate: null,
        version: alvo.version + 1,
      });
      return route.fulfill({
        json: { recurrence: alvo, deletedCount: 15, deletionBatchId: 'b1' },
      });
    }
    if (!acao && req.method() === 'DELETE') {
      lista.splice(lista.indexOf(alvo), 1);
      return route.fulfill({ json: { deletedCount: 15, unlinkedCount: 1 } });
    }
    return route.fulfill(problema(404, 'not_found', 'Não encontrada.'));
  });
  return lista;
}

async function preencherTim(page: Page, mesInicial: string, valor = '44,99') {
  await page.getByLabel('Descrição').fill('Tim');
  await page.getByLabel('Valor', { exact: true }).fill(valor);
  await page.getByLabel('Dia do mês').fill('10');
  await page.getByLabel('Mês inicial').fill(mesInicial);
}

async function informarMes(page: Page, mes: string) {
  const dialogo = page.getByRole('dialog');
  await dialogo.getByTestId('a-partir-de-mes').fill(mes);
  await dialogo.getByTestId('confirmar').click();
  await expect(dialogo).toHaveCount(0);
}

test.describe('recorrências (005 US1, US3) — API simulada', () => {
  test('cria "Tim", altera a partir de um mês, encerra e exclui', async ({ page }) => {
    await simularApi(page);
    await page.goto('/recorrencias');
    await expect(page.getByTestId('lista-vazia')).toBeVisible();

    // criar
    await page.getByRole('link', { name: 'Nova recorrência' }).click();
    await expect(page).toHaveURL(/\/recorrencias\/nova$/);
    await preencherTim(page, '2026-03');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page).toHaveURL(/\/recorrencias$/);
    const linha = page.getByTestId('recorrencia').filter({ hasText: 'Tim' });
    await expect(linha).toContainText('R$ 44,99');
    await expect(linha).toContainText('Dia 10');
    await expect(linha).toContainText('Azul');
    await expect(page.getByTestId('total-saidas')).toContainText('R$ 44,99');
    await expect(page.getByTestId('total-entradas')).toContainText('R$ 0,00');

    // alterar a partir de setembro
    await linha.click();
    await expect(page).toHaveURL(/\/recorrencias\/r1$/);
    await page.getByLabel('Valor', { exact: true }).fill('50,99');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await informarMes(page, '2026-09');
    await expect(page.getByTestId('info-recorrencia')).toContainText('a partir de 2026-09');
    await expect(page.getByLabel('Valor', { exact: true })).toHaveValue('50,99');

    // encerrar a partir de outubro
    await page.getByRole('button', { name: 'Encerrar' }).click();
    await expect(page.getByRole('dialog')).toContainText('Encerrar "Tim"?');
    await informarMes(page, '2026-10');
    await expect(page.getByTestId('situacao')).toHaveText('Encerrada');
    await expect(page.getByTestId('info-recorrencia')).toContainText('excluído(s)');
    await expect(page.getByRole('button', { name: 'Encerrar' })).toHaveCount(0);

    await page.getByRole('link', { name: 'Voltar' }).click();
    await expect(page.getByTestId('lista-vazia')).toBeVisible();
    await page.getByTestId('filtro-encerradas').click();
    const encerrada = page.getByTestId('recorrencia').filter({ hasText: 'Tim' });
    await expect(encerrada).toContainText('R$ 50,99');
    await expect(encerrada).toContainText('encerrada');

    // excluir
    await encerrada.click();
    await page.getByRole('button', { name: 'Excluir' }).click();
    const dialogo = page.getByRole('dialog');
    await dialogo.getByRole('button', { name: 'Cancelar' }).click();
    await expect(dialogo).toHaveCount(0);
    await expect(page).toHaveURL(/\/recorrencias\/r1$/);
    await page.getByRole('button', { name: 'Excluir' }).click();
    await page.getByRole('dialog').getByTestId('confirmar').click();
    await expect(page).toHaveURL(/\/recorrencias$/);
    await page.getByTestId('filtro-encerradas').click();
    await expect(page.getByTestId('lista-vazia')).toBeVisible();
  });

  test('valida o formulário antes de enviar e mostra o erro do servidor', async ({ page }) => {
    await simularApi(page);
    await page.goto('/recorrencias/nova');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByTestId('erro-descricao')).toBeVisible();
    await expect(page.getByTestId('erro-valor')).toBeVisible();
    await expect(page).toHaveURL(/\/recorrencias\/nova$/);

    await page.getByTestId('termino-after_count').click();
    await page.getByLabel('Descrição').fill('Curso');
    await page.getByLabel('Valor', { exact: true }).fill('10');
    await page.getByLabel('Número de ocorrências').fill('400');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByTestId('erro-termino')).toContainText('1 a 360');
  });

  test('"Recorrências" é alcançável pela navegação da casca (em "Mais" no celular)', async ({
    page,
  }) => {
    await simularApi(page);
    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    await irPara(page, 'Recorrências');
    await expect(page).toHaveURL(/\/recorrencias$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Recorrências' })).toBeVisible();
  });
});

// Contra a stack real (E2E_BASE_URL); sem ela, são pulados.
test.describe('recorrências (005) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  test('cria, vê as ocorrências no mês, altera a partir de um mês, encerra e exclui', async ({
    page,
  }) => {
    await criarEspacoReal(page);
    const m0 = mesDaquiA(0);
    const m1 = mesDaquiA(1);
    const m2 = mesDaquiA(2);
    const tim = () => page.getByTestId('lancamento').filter({ hasText: 'Tim' });

    await page.goto('/recorrencias/nova');
    await preencherTim(page, m0);
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page).toHaveURL(/\/recorrencias$/);
    await expect(page.getByTestId('recorrencia').filter({ hasText: 'Tim' })).toContainText(
      'R$ 44,99',
    );

    // as ocorrências já existem nos meses, com o ícone de recorrência
    await page.goto(`/mes/${m0}`);
    await expect(tim()).toContainText('R$ 44,99');
    await expect(tim().getByTestId('selo-recorrencia')).toBeVisible();
    await expect(tim().getByTestId('selo-alterada')).toHaveCount(0);

    // alterar o valor a partir do mês seguinte: o mês atual não muda
    await page.goto('/recorrencias');
    await page.getByTestId('recorrencia').filter({ hasText: 'Tim' }).click();
    await page.getByLabel('Valor', { exact: true }).fill('50,99');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await informarMes(page, m1);
    await expect(page.getByTestId('info-recorrencia')).toContainText(`a partir de ${m1}`);
    await page.goto(`/mes/${m0}`);
    await expect(tim()).toContainText('R$ 44,99');
    await page.goto(`/mes/${m1}`);
    await expect(tim()).toContainText('R$ 50,99');

    // encerrar a partir de m2: o último mês passa a ser m1; m2 some e m1 continua
    await page.goto('/recorrencias');
    await page.getByTestId('recorrencia').filter({ hasText: 'Tim' }).click();
    await page.getByRole('button', { name: 'Encerrar' }).click();
    await informarMes(page, m2);
    await expect(page.getByTestId('info-recorrencia')).toContainText('excluído(s)');
    await expect(page.getByTestId('situacao')).toHaveText(`Ativa até ${m1}`);
    await page.goto(`/mes/${m2}`);
    await expect(page.getByTestId('mes-vazio')).toBeVisible();
    await page.goto(`/mes/${m1}`);
    await expect(tim()).toContainText('R$ 50,99');

    // excluir a recorrência
    await page.goto('/recorrencias');
    await page.getByTestId('recorrencia').filter({ hasText: 'Tim' }).click();
    await page.getByRole('button', { name: 'Excluir' }).click();
    await page.getByRole('dialog').getByTestId('confirmar').click();
    await expect(page).toHaveURL(/\/recorrencias$/);
    await expect(page.getByTestId('lista-vazia')).toBeVisible();
  });

  test('repete todo mês um lançamento: ele vira a ocorrência do mês e as seguintes aparecem', async ({
    page,
  }) => {
    const espaco = await criarEspacoReal(page);
    const m0 = mesDaquiA(0);
    const m1 = mesDaquiA(1);
    const m2 = mesDaquiA(2);
    const m3 = mesDaquiA(3);
    await espaco.entrada('expense', 'Seguro Vida', '25,99', `${m0}-10`);
    const seguro = () => page.getByTestId('lancamento').filter({ hasText: 'Seguro Vida' });

    await page.goto(`/mes/${m0}`);
    await expect(seguro().getByTestId('selo-recorrencia')).toHaveCount(0);
    await page.getByRole('button', { name: 'Seguro Vida' }).first().click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeVisible();
    await page.getByTestId('repetir').click();
    await page.getByTestId('repetir-fim').selectOption('until_month');
    await page.getByTestId('repetir-mes').fill(m2);
    await page.getByTestId('repetir-confirmar').click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeHidden();

    // o original é a ocorrência de m0 (sem duplicar) e m1 e m2 ganharam as seguintes
    await expect(seguro()).toHaveCount(1);
    await expect(seguro().getByTestId('selo-recorrencia')).toBeVisible();
    await expect(seguro().getByTestId('selo-alterada')).toHaveCount(0);
    await page.goto(`/mes/${m1}`);
    await expect(seguro()).toContainText('R$ 25,99');
    await expect(seguro().getByTestId('selo-recorrencia')).toBeVisible();
    await page.goto(`/mes/${m2}`);
    await expect(seguro()).toHaveCount(1);
    await page.goto(`/mes/${m3}`);
    await expect(seguro()).toHaveCount(0);

    // a recorrência aparece na tela de recorrências e a ocorrência não oferece repetir de novo
    await page.goto('/recorrencias');
    await expect(page.getByTestId('recorrencia').filter({ hasText: 'Seguro Vida' })).toContainText(
      'Dia 10',
    );
    await page.goto(`/mes/${m1}`);
    await page.getByRole('button', { name: 'Seguro Vida' }).first().click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeVisible();
    await expect(page.getByTestId('repetir')).toHaveCount(0);
  });
});
