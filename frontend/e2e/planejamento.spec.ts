import { expect, test, type Page } from '@playwright/test';
import { irPara } from './navegacao';
import { carteiraAzul, sessao } from './tags-api';

// 007 T005 — Tela /planejamento/:ano (US1). API simulada em memória: a grade é calculada a partir dos
// lançamentos como o servidor faz (linha por descrição, célula = soma, totais = Entradas − Saídas).
// Computador: edição inline, Nova linha, Preencher meses, lista de célula. Celular: edição por diálogo.

const HOJE = '2027-03-10T12:00:00-03:00';
const MESES = Array.from({ length: 12 }, (_, i) => `2027-${String(i + 1).padStart(2, '0')}`);

interface Item {
  id: string;
  kind: 'income' | 'expense';
  description: string;
  month: string;
  cents: number;
  status: 'planned' | 'settled';
}

const normalizar = (texto: string) =>
  texto
    .trim()
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .toLowerCase();

const centavos = (texto: string) =>
  Math.round(Number(texto.replace(/^=/, '').replace(/\./g, '').replace(',', '.')) * 100);

async function simular(page: Page, itens: Item[]) {
  let seq = 100;
  const estimativas = new Map<string, number>();

  const linhas = (kind: 'income' | 'expense') => {
    const chaves = [
      ...new Set(itens.filter((i) => i.kind === kind).map((i) => normalizar(i.description))),
    ].sort();
    return chaves.map((chave) => {
      const doGrupo = itens.filter((i) => i.kind === kind && normalizar(i.description) === chave);
      return {
        kind,
        key: `${kind}:${chave}`,
        description: doGrupo[doGrupo.length - 1].description,
        latestWalletId: 'w-azul',
        latestDay: 10,
        latestTagIds: [],
        cells: MESES.map((month) => {
          const da = doGrupo.filter((i) => i.month === month);
          const unico = da.length === 1 && da[0].status === 'planned';
          return {
            month,
            plannedCents: da.reduce((s, i) => s + i.cents, 0),
            itemCount: da.length,
            allSettled: da.length > 0 && da.every((i) => i.status === 'settled'),
            editable: da.length === 0 || unico,
            entryId: da.length === 1 ? da[0].id : null,
          };
        }),
      };
    });
  };
  const fatura = () => ({
    creditCardId: 'c-azul',
    creditCardName: 'Azul',
    archived: false,
    cells: MESES.map((month) => ({
      month,
      plannedCents: estimativas.get(month) ?? 0,
      totalCents: 0,
      estimateCents: estimativas.get(month) ?? null,
      editable: true,
    })),
  });
  const totais = () => {
    let saldo = 0;
    return {
      months: MESES.map((month) => {
        const soma = (kind: string) =>
          itens
            .filter((i) => i.kind === kind && i.month === month)
            .reduce((s, i) => s + i.cents, 0);
        const income = soma('income');
        const expense = soma('expense') + (estimativas.get(month) ?? 0);
        saldo += income - expense;
        return {
          month,
          incomeCents: income,
          expenseCents: expense,
          resultCents: income - expense,
          finalBalanceCents: saldo,
        };
      }),
    };
  };
  const linhaDe = (kind: 'income' | 'expense', descricao: string) =>
    linhas(kind).find((l) => l.key === `${kind}:${normalizar(descricao)}`) ?? {
      kind,
      key: `${kind}:${normalizar(descricao)}`,
      description: descricao,
      latestWalletId: 'w-azul',
      latestDay: 10,
      latestTagIds: [],
      cells: MESES.map((month) => ({
        month,
        plannedCents: 0,
        itemCount: 0,
        allSettled: false,
        editable: true,
      })),
    };

  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) => route.fulfill({ json: [carteiraAzul] }));
  await page.route('**/api/v1/years/2027/planning', (route) =>
    route.fulfill({
      json: {
        year: 2027,
        today: '2027-03-10',
        incomeRows: linhas('income'),
        expenseRows: linhas('expense'),
        invoiceRows: [fatura()],
        totals: totais(),
      },
    }),
  );
  await page.route('**/api/v1/years/2027/planning/cells', async (route) => {
    const c = route.request().postDataJSON();
    if (c.kind === 'invoice') {
      if (c.value.trim() === '') estimativas.delete(c.month);
      else estimativas.set(c.month, centavos(c.value));
      return route.fulfill({ json: { row: fatura(), totals: totais() } });
    }
    const da = itens.filter(
      (i) =>
        i.kind === c.kind &&
        normalizar(i.description) === normalizar(c.description) &&
        i.month === c.month,
    );
    if (da.length > 1 || da.some((i) => i.status === 'settled')) {
      return route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        json: {
          code: 'planning.cell_not_editable',
          detail: 'Esta célula tem mais de um lançamento ou algum já efetivado.',
          items: da.map((i) => ({
            id: i.id,
            description: i.description,
            amountCents: i.cents,
            date: `${i.month}-10`,
            status: i.status,
            walletId: 'w-azul',
          })),
        },
      });
    }
    if (c.value.trim() === '') {
      if (da[0]) itens.splice(itens.indexOf(da[0]), 1);
    } else if (da[0]) {
      da[0].cents = centavos(c.value);
    } else {
      itens.push({
        id: `e-${seq++}`,
        kind: c.kind,
        description: c.description,
        month: c.month,
        cents: centavos(c.value),
        status: 'planned',
      });
    }
    return route.fulfill({
      json: { row: linhaDe(c.kind, c.description), totals: totais() },
    });
  });
  await page.route('**/api/v1/years/2027/planning/fill', async (route) => {
    const f = route.request().postDataJSON();
    let created = 0;
    let skipped = 0;
    for (const month of f.months as string[]) {
      const ocupado = itens.some(
        (i) =>
          i.kind === f.kind &&
          normalizar(i.description) === normalizar(f.description) &&
          i.month === month,
      );
      if (ocupado) skipped++;
      else {
        created++;
        itens.push({
          id: `e-${seq++}`,
          kind: f.kind,
          description: f.description,
          month,
          cents: centavos(f.amount),
          status: 'planned',
        });
      }
    }
    return route.fulfill({
      json: { created, skipped, row: linhaDe(f.kind, f.description), totals: totais() },
    });
  });
}

const item = (
  id: string,
  kind: Item['kind'],
  description: string,
  month: number,
  cents: number,
  status: Item['status'] = 'planned',
): Item => ({
  id,
  kind,
  description,
  month: MESES[month - 1],
  cents,
  status,
});

const celula = (page: Page, linha: string, mes: string) =>
  page.locator(`[data-linha="${linha}"] [data-testid="celula"][data-mes="${mes}"]`);
const total = (page: Page, qual: string, mes: string) =>
  page.locator(`[data-testid="total-${qual}"] [data-mes="${mes}"]`);

test.describe('planejamento do ano (US1) — API simulada', () => {
  test('US1.1: blocos, 12 meses e as linhas de total', async ({ page }) => {
    await simular(page, [
      item('e1', 'income', 'Salário', 1, 500000),
      item('e2', 'expense', 'Tim', 1, 4499),
    ]);
    await page.goto('/planejamento/2027');
    await expect(page.getByTestId('planejamento-titulo')).toHaveText('Planejamento 2027');
    await expect(page.getByTestId('mes-coluna')).toHaveCount(12);
    await expect(page.getByTestId('bloco-entradas').getByTestId('linha-nome')).toHaveText([
      'Salário',
    ]);
    await expect(page.getByTestId('bloco-saidas').getByTestId('linha-nome')).toHaveText(['Tim']);
    await expect(page.getByTestId('bloco-faturas').getByTestId('linha-nome')).toHaveText([
      'Fatura Azul',
    ]);
    await expect(celula(page, 'income:salario', '2027-01')).toContainText('5.000,00');
    await expect(total(page, 'resultado', '2027-01')).toContainText('4.955,01');
    await expect(page.locator('th.hoje')).toHaveText('MAR');
  });

  test('"Planejamento" está no menu', async ({ page }) => {
    await simular(page, []);
    await page.goto('/mes');
    await expect(page.getByRole('navigation', { name: 'Principal' })).toBeVisible();
    await irPara(page, 'Planejamento');
    await expect(page).toHaveURL(/\/planejamento\/\d{4}$/);
  });

  test('Teste independente da US1: Natal 1.200 e 13º salário 1.500 em dezembro somam +300 no resultado', async ({
    page,
  }, info) => {
    test.skip(info.project.name === 'mobile', 'edição inline é do computador');
    await simular(page, []);
    await page.goto('/planejamento/2027');
    await expect(total(page, 'resultado', '2027-12')).toContainText('0,00');

    const novaLinha = async (tipo: 'saida' | 'entrada', descricao: string) => {
      await page.getByTestId('nova-linha').click();
      await page.getByTestId(`nova-linha-${tipo}`).click();
      await page.getByTestId('nova-linha-descricao').fill(descricao);
      await page.getByTestId('nova-linha-criar').click();
    };

    await novaLinha('saida', 'Natal');
    await expect(page.getByTestId('bloco-saidas').getByTestId('linha-nome')).toHaveText(['Natal']);
    await celula(page, 'expense:natal', '2027-12').getByTestId('celula-valor').click();
    await page.getByTestId('celula-campo').fill('1200');
    await page.getByTestId('celula-campo').press('Enter');
    await expect(celula(page, 'expense:natal', '2027-12')).toContainText('1.200,00');
    await expect(total(page, 'resultado', '2027-12')).toContainText('-R$ 1.200,00');

    await novaLinha('entrada', '13º salário');
    await celula(page, 'income:13º salario', '2027-12').getByTestId('celula-valor').click();
    await page.getByTestId('celula-campo').fill('1500');
    await page.getByTestId('celula-campo').press('Enter');
    await expect(celula(page, 'income:13º salario', '2027-12')).toContainText('1.500,00');
    await expect(total(page, 'resultado', '2027-12')).toHaveText('R$ 300,00');
    await expect(total(page, 'entradas', '2027-12')).toContainText('1.500,00');
    await expect(total(page, 'saidas', '2027-12')).toContainText('1.200,00');
  });

  test('US1.3, US1.4 e US1.6: alterar com expressão, apagar e desfazer', async ({ page }, info) => {
    test.skip(info.project.name === 'mobile', 'edição inline é do computador');
    await simular(page, [item('e2', 'expense', 'Tim', 3, 4499)]);
    await page.goto('/planejamento/2027');

    await celula(page, 'expense:tim', '2027-03').getByTestId('celula-valor').click();
    const campo = page.getByTestId('celula-campo');
    await expect(campo).toHaveValue('44,99');
    await campo.fill('=50,99');
    await campo.press('Enter');
    await expect(celula(page, 'expense:tim', '2027-03')).toContainText('50,99');
    await expect(total(page, 'saidas', '2027-03')).toContainText('50,99');

    await celula(page, 'expense:tim', '2027-03').getByTestId('celula-valor').click();
    await expect(page.getByTestId('celula-campo')).toHaveValue('50,99');
    await page.getByTestId('celula-campo').fill('');
    await expect(page.getByTestId('celula-campo')).toHaveValue('');
    await page.getByTestId('celula-campo').press('Enter');
    await expect(total(page, 'saidas', '2027-03')).toHaveText('R$ 0,00');
  });

  test('Tab salva e vai para o mês seguinte; Esc cancela', async ({ page }, info) => {
    test.skip(info.project.name === 'mobile', 'edição inline é do computador');
    await simular(page, [item('e2', 'expense', 'Tim', 3, 4499)]);
    await page.goto('/planejamento/2027');
    await celula(page, 'expense:tim', '2027-05').getByTestId('celula-valor').click();
    await page.getByTestId('celula-campo').fill('10');
    await page.getByTestId('celula-campo').press('Tab');
    await expect(celula(page, 'expense:tim', '2027-05')).toContainText('10,00');
    await expect(celula(page, 'expense:tim', '2027-06').getByTestId('celula-campo')).toBeFocused();
    await page.getByTestId('celula-campo').fill('77');
    await page.getByTestId('celula-campo').press('Escape');
    await expect(celula(page, 'expense:tim', '2027-06')).not.toContainText('77');
  });

  test('US1.5: célula com 2 lançamentos abre a lista, sem alterar nada', async ({ page }) => {
    await simular(page, [
      item('e1', 'expense', 'Mercado', 3, 10000),
      item('e2', 'expense', 'Mercado', 3, 5000),
    ]);
    await page.goto('/planejamento/2027');
    await expect(celula(page, 'expense:mercado', '2027-03')).toContainText('150,00');
    await celula(page, 'expense:mercado', '2027-03').getByTestId('celula-valor').click();
    await expect(page.getByTestId('item-celula')).toHaveCount(2);
    await expect(page.getByTestId('itens-aviso')).toContainText('individualmente');
    await page.getByTestId('fechar').click();
    await expect(celula(page, 'expense:mercado', '2027-03')).toContainText('150,00');
  });

  test('célula com lançamento efetivado fica marcada e abre a lista', async ({ page }) => {
    await simular(page, [item('e1', 'expense', 'Luz', 2, 12000, 'settled')]);
    await page.goto('/planejamento/2027');
    await expect(celula(page, 'expense:luz', '2027-02')).toHaveClass(/efetivado/);
    await celula(page, 'expense:luz', '2027-02').getByTestId('celula-valor').click();
    await expect(page.getByTestId('item-celula')).toHaveCount(1);
    await expect(page.getByTestId('item-celula')).toContainText('Efetivado');
  });

  test('US2.4: valor na linha do cartão define a estimativa', async ({ page }, info) => {
    test.skip(info.project.name === 'mobile', 'edição inline é do computador');
    await simular(page, []);
    await page.goto('/planejamento/2027');
    await celula(page, 'invoice:c-azul', '2027-05').getByTestId('celula-valor').click();
    await page.getByTestId('celula-campo').fill('800');
    await page.getByTestId('celula-campo').press('Enter');
    await expect(celula(page, 'invoice:c-azul', '2027-05')).toContainText('800,00');
    await expect(
      celula(page, 'invoice:c-azul', '2027-05').getByTestId('celula-estimativa'),
    ).toBeVisible();
    await expect(total(page, 'saidas', '2027-05')).toContainText('800,00');
  });

  test('US1.8: Preencher meses cria nos meses vazios e informa os pulados', async ({
    page,
  }, info) => {
    test.skip(info.project.name === 'mobile', 'Preencher meses é do computador');
    await simular(page, [item('e1', 'expense', 'Internet', 2, 9990)]);
    await page.goto('/planejamento/2027');
    await page.locator('[data-linha="expense:internet"]').getByTestId('linha-menu').click();
    await page.getByTestId('menu-preencher').click();
    await page.locator('app-amount-input input').fill('300');
    await page.getByTestId('preencher-confirmar').click();
    await expect(page.getByTestId('aviso-planejamento')).toHaveText(
      '11 meses preenchidos; 1 mês já tinha valor e foi pulado.',
    );
    await expect(celula(page, 'expense:internet', '2027-01')).toContainText('300,00');
    await expect(celula(page, 'expense:internet', '2027-02')).toContainText('99,90');
    await expect(total(page, 'saidas', '2027-12')).toContainText('300,00');
  });

  test('US1.9: Transformar em recorrência abre o cadastro preenchido', async ({ page }) => {
    await simular(page, [
      item('e1', 'expense', 'Tim', 1, 4499),
      item('e2', 'expense', 'Tim', 2, 4499),
      item('e3', 'expense', 'Tim', 3, 5099),
    ]);
    await page.route('**/api/v1/credit-cards**', (route) => route.fulfill({ json: [] }));
    await page.route('**/api/v1/tags**', (route) => route.fulfill({ json: [] }));
    await page.goto('/planejamento/2027');
    await page.locator('[data-linha="expense:tim"]').getByTestId('linha-menu').click();
    await page.getByTestId('menu-recorrencia').click();
    await expect(page).toHaveURL(/\/recorrencias\/nova\?/);
    await expect(page.locator('#descricao')).toHaveValue('Tim');
    await expect(page.locator('#amount-field')).toHaveValue('44,99');
    await expect(page.locator('#dia')).toHaveValue('10');
    await expect(page.locator('#mes-inicial')).toHaveValue('2027-01');
  });

  test('celular: a célula é editada por diálogo e "Preencher meses" não aparece', async ({
    page,
  }, info) => {
    test.skip(info.project.name !== 'mobile', 'versão celular');
    await simular(page, [item('e2', 'expense', 'Tim', 3, 4499)]);
    await page.goto('/planejamento/2027');
    await expect(page.getByTestId('planejamento-tabela')).toBeVisible();

    await celula(page, 'expense:tim', '2027-05').getByTestId('celula-valor').click();
    await expect(page.getByTestId('celula-campo')).toHaveCount(0);
    await page.locator('app-celula-dialog input').fill('70');
    await page.getByTestId('celula-salvar').click();
    await expect(celula(page, 'expense:tim', '2027-05')).toContainText('70,00');
    await expect(total(page, 'saidas', '2027-05')).toContainText('70,00');

    await page.locator('[data-linha="expense:tim"]').getByTestId('linha-menu').click();
    await expect(page.getByTestId('menu-recorrencia')).toBeVisible();
    await expect(page.getByTestId('menu-preencher')).toHaveCount(0);

    // a 1ª coluna fica fixa ao rolar os meses
    const rotulo = page.locator('[data-linha="expense:tim"] th.rotulo');
    await page.keyboard.press('Escape');
    const antes = (await rotulo.boundingBox())!.x;
    await page.locator('.tabela-rolavel').evaluate((el) => (el.scrollLeft = 400));
    const depois = (await rotulo.boundingBox())!.x;
    expect(Math.abs(depois - antes)).toBeLessThan(2);
  });
});
