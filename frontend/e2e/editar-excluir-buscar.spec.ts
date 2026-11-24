import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { dispensarGuiaSeAberto } from './espaco-novo';
import { comecarEmSaida } from './navegacao';

// US6.1–US6.5 contra a stack real: editar (RT-04), excluir com Desfazer (RT-09), duplicar (FR-024)
// e buscar (FR-025). Rodam nos dois viewports. Precisam do compose no ar e de `E2E_BASE_URL`.
test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (defina E2E_BASE_URL)');

interface Lancamento {
  id: string;
  description: string;
  date: string;
  referenceMonth: string;
  status: string;
  version: number;
  amountCents: number;
}

/** "Hoje" no fuso de São Paulo, como o servidor (FR-012). */
const hoje = () => new Date().toLocaleDateString('en-CA', { timeZone: 'America/Sao_Paulo' });

async function criarConta(page: Page): Promise<string> {
  const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@exemplo.com`;
  await comecarEmSaida(page);
  await page.goto('/criar-conta');
  await page.getByLabel('Nome').fill('Lia');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha', { exact: true }).fill('segredo123');
  await page.getByRole('button', { name: 'Criar conta' }).click();
  await dispensarGuiaSeAberto(page);
  const resposta = await page.request.post('/api/v1/auth/refresh');
  return (await resposta.json()).accessToken as string;
}

function api(request: APIRequestContext, token: string) {
  const headers = { Authorization: `Bearer ${token}` };
  const chamar = async <T>(metodo: 'post' | 'get' | 'put', url: string, data?: object) => {
    const r = await request[metodo](`/api/v1${url}`, { headers, data });
    expect(r.ok(), `${metodo} ${url} → ${r.status()} ${await r.text()}`).toBe(true);
    return (await r.json()) as T;
  };
  return {
    carteira: (name: string) =>
      chamar<{ id: string }>('post', '/wallets', {
        name,
        type: 'checking',
        color: '#1E88E5',
        initialBalance: '500,00',
        initialBalanceDate: '2026-01-01',
      }),
    lancar: (walletId: string, description: string, amount: string, date: string, extra = {}) =>
      chamar<Lancamento>('post', '/entries', {
        kind: 'expense',
        description,
        amount,
        date,
        walletId,
        ...extra,
      }),
    ler: (id: string) => chamar<Lancamento>('get', `/entries/${id}`),
    editar: (id: string, corpo: object) => chamar<Lancamento>('put', `/entries/${id}`, corpo),
    transferir: (from: string, to: string, amount: string, date: string) =>
      chamar<{ id: string }>('post', '/transfers', {
        fromWalletId: from,
        toWalletId: to,
        amount,
        date,
      }),
  };
}

async function abrirLancamento(page: Page, descricao: string) {
  await page.getByRole('button', { name: descricao }).first().click();
  await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeVisible();
}

test.describe('editar, excluir, duplicar e buscar (US6)', () => {
  test('US6.1: mudar a data para outro mês leva o mês de referência junto', async ({
    page,
    request,
  }) => {
    const a = api(request, await criarConta(page));
    const azul = await a.carteira('Azul');
    const tim = await a.lancar(azul.id, 'Tim', '44,99', '2026-03-02');
    expect(tim.referenceMonth).toBe('2026-03');

    await page.goto('/mes/2026-03');
    await abrirLancamento(page, 'Tim');
    await page.getByLabel('Data').fill('2026-04-05');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeHidden();

    const editado = await a.ler(tim.id);
    expect(editado.date).toBe('2026-04-05');
    expect(editado.referenceMonth).toBe('2026-04');
    await expect(page.getByRole('button', { name: 'Tim' })).toHaveCount(0);
  });

  test('US6.2: mês de referência escolhido à mão é mantido ao mudar a data', async ({
    page,
    request,
  }) => {
    const a = api(request, await criarConta(page));
    const azul = await a.carteira('Azul');
    const luz = await a.lancar(azul.id, 'Luz', '120,00', '2026-02-25', {
      referenceMonth: '2026-03',
    });
    expect(luz.referenceMonth).toBe('2026-03');

    await page.goto('/mes/2026-03');
    await abrirLancamento(page, 'Luz');
    await page.getByLabel('Data').fill('2026-02-26');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeHidden();

    const editado = await a.ler(luz.id);
    expect(editado.date).toBe('2026-02-26');
    expect(editado.referenceMonth).toBe('2026-03');
  });

  test('conflito: salvar depois de outro aparelho alterar avisa e recarrega', async ({
    page,
    request,
  }) => {
    const a = api(request, await criarConta(page));
    const azul = await a.carteira('Azul');
    const tim = await a.lancar(azul.id, 'Tim', '44,99', '2026-03-10');

    await page.goto('/mes/2026-03');
    await abrirLancamento(page, 'Tim');
    // "Outro aparelho" muda a descrição enquanto o formulário está aberto.
    await a.editar(tim.id, {
      kind: 'expense',
      description: 'Tim Móvel',
      amount: '44,99',
      date: '2026-03-10',
      walletId: azul.id,
      status: tim.status,
      version: tim.version,
    });
    await page.getByLabel('Descrição').fill('Tim Fixo');
    await page.getByTestId('salvar').click();

    await expect(page.getByTestId('erro-lancamento')).toContainText('alterado em outro aparelho');
    await expect(page.getByLabel('Descrição')).toHaveValue('Tim Móvel');

    // Com a versão recarregada, salvar funciona.
    await page.getByLabel('Descrição').fill('Tim Fixo');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeHidden();
    expect((await a.ler(tim.id)).description).toBe('Tim Fixo');
  });

  test('US6.3: excluir mostra "Desfazer" e desfazer traz o lançamento de volta idêntico', async ({
    page,
    request,
  }) => {
    const a = api(request, await criarConta(page));
    const azul = await a.carteira('Azul');
    const tim = await a.lancar(azul.id, 'Tim', '44,99', '2026-03-10');

    await page.goto('/mes/2026-03');
    await abrirLancamento(page, 'Tim');
    await page.getByTestId('excluir').click();

    await expect(page.getByRole('button', { name: 'Tim' })).toHaveCount(0);
    await expect(page.getByText('Excluído')).toBeVisible();
    await page.getByRole('button', { name: 'Desfazer' }).click();

    await expect(page.getByRole('button', { name: 'Tim' })).toBeVisible();
    const volta = await a.ler(tim.id);
    expect(volta).toMatchObject({
      description: 'Tim',
      amountCents: 4499,
      date: '2026-03-10',
      referenceMonth: '2026-03',
    });
  });

  test('transferência: excluir na lista do mês também oferece Desfazer', async ({
    page,
    request,
  }) => {
    const a = api(request, await criarConta(page));
    const azul = await a.carteira('Azul');
    const verde = await a.carteira('Verde');
    await a.transferir(azul.id, verde.id, '100,00', '2026-03-08');

    await page.goto('/mes/2026-03');
    await page.getByTestId('transferencias-alternar').click();
    await expect(page.getByTestId('transferencia')).toHaveCount(1);
    await page.getByTestId('excluir-transferencia').click();
    await expect(page.getByTestId('transferencias-alternar')).toHaveCount(0);

    await page.getByRole('button', { name: 'Desfazer' }).click();
    await expect(page.getByTestId('transferencias-alternar')).toBeVisible();
  });

  test('US6.4: duplicar pede só a nova data; mês e situação são recalculados', async ({
    page,
    request,
  }) => {
    const a = api(request, await criarConta(page));
    const azul = await a.carteira('Azul');
    await a.lancar(azul.id, 'Tim', '44,99', '2026-03-10');

    await page.goto('/mes/2026-03');
    await abrirLancamento(page, 'Tim');
    await page.getByTestId('duplicar').click();
    await page.getByLabel('Nova data').fill('2026-04-10');
    await page.getByTestId('duplicar-confirmar').click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeHidden();

    await page.goto('/mes/2026-04');
    await expect(page.getByRole('button', { name: 'Tim' })).toBeVisible();
    await abrirLancamento(page, 'Tim');
    await expect(page.getByLabel('Data')).toHaveValue('2026-04-10');
    await expect(page.getByLabel('Descrição')).toHaveValue('Tim');
    // Original continua em março.
    await page.goto('/mes/2026-03');
    await expect(page.getByRole('button', { name: 'Tim' })).toBeVisible();

    const esperada = '2026-04-10' <= hoje() ? 'settled' : 'planned';
    await page.goto('/mes/2026-04');
    await abrirLancamento(page, 'Tim');
    await expect(
      page.getByRole('radio', { name: esperada === 'settled' ? 'Paga' : 'Prevista' }),
    ).toHaveAttribute('aria-checked', 'true');
  });

  test('US6.5: busca sem diferenciar maiúsculas e acentos, de vários meses, com total', async ({
    page,
    request,
  }) => {
    const a = api(request, await criarConta(page));
    const azul = await a.carteira('Azul');
    // Descrições únicas por execução: a busca não pode depender de dados de outros testes.
    const u = `${Date.now()}${Math.floor(Math.random() * 1e6)}`;
    const mercado = `Mercado ${u}`;
    await a.lancar(azul.id, `Uber Centro ${u}`, '25,50', '2026-01-15');
    await a.lancar(azul.id, `UBER Aeroporto ${u}`, '40,00', '2026-02-20');
    await a.lancar(azul.id, `Café Verde ${u}`, '12,00', '2026-03-03');
    await a.lancar(azul.id, mercado, '99,90', '2026-03-04');
    await a.transferir(azul.id, (await a.carteira('Verde')).id, '10,00', '2026-03-05');

    await page.goto('/busca');
    await expect(page.getByTestId('resultado')).toHaveCount(4);

    await page.getByLabel('Descrição').fill('uber');
    await expect(page.getByTestId('resultado')).toHaveCount(2);
    await expect(page.getByTestId('total')).toContainText('2 resultados');
    await expect(page.getByTestId('total-valor')).toContainText('R$ 65,50');
    await expect(page.getByTestId('total-valor')).toContainText('-');

    await page.getByLabel('Descrição').fill('cafe');
    await expect(page.getByTestId('resultado')).toHaveCount(1);
    await expect(page.getByTestId('resultado')).toContainText(`Café Verde ${u}`);

    // Filtro de tipo: só transferências.
    await page.getByLabel('Descrição').fill('');
    await page.getByTestId('tipo-transfer').click();
    await expect(page.getByTestId('resultado-transferencia')).toHaveCount(1);
    await expect(page.getByTestId('resultado')).toHaveCount(0);

    // Tocar num resultado abre a edição.
    await page.getByTestId('tipo-transfer').click();
    await page.getByLabel('Descrição').fill(mercado.toLowerCase());
    const resultado = page.getByTestId('resultado').filter({ hasText: mercado });
    await expect(page.getByTestId('resultado')).toHaveCount(1);
    await resultado.getByRole('button', { name: new RegExp(mercado) }).click();
    await expect(page.getByRole('heading', { name: 'Editar lançamento' })).toBeVisible();
  });

  test('US6.5: rolagem infinita pelo cursor traz todas as páginas e o total é de todos', async ({
    page,
    request,
  }) => {
    const a = api(request, await criarConta(page));
    const azul = await a.carteira('Azul');
    const total = 55;
    for (let i = 0; i < total; i += 11) {
      await Promise.all(
        Array.from({ length: 11 }, (_, k) =>
          a.lancar(
            azul.id,
            `Padaria ${i + k}`,
            '10,00',
            `2026-03-${String(1 + ((i + k) % 28)).padStart(2, '0')}`,
          ),
        ),
      );
    }

    await page.goto('/busca');
    await page.getByLabel('Descrição').fill('padaria');
    await expect(page.getByTestId('total')).toContainText('55 resultados');
    await expect(page.getByTestId('total-valor')).toContainText('R$ 550,00');
    await page.getByTestId('resultados').scrollIntoViewIfNeeded();
    await page.mouse.wheel(0, 100_000);
    await expect(page.getByTestId('resultado')).toHaveCount(55);
  });
});
