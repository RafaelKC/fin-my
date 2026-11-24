import { expect, test, type Page, type Route } from '@playwright/test';
import { dispensarGuiaSeAberto } from './espaco-novo';
import { comecarEmSaida } from './navegacao';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};

/** "Hoje" fixo do cenário da spec (US2): 10/03/2026. */
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
  hasEntries: false,
  version: 1,
};
const verde = {
  ...azul,
  id: 'w-verde',
  name: 'Verde',
  color: '#43A047',
  isPrimary: false,
  initialBalanceCents: 0,
  initialBalanceDate: '2026-01-01',
  currentBalanceCents: 0,
};

interface Corpo {
  kind: string;
  description: string;
  amount: string;
  date: string;
  walletId: string;
  status?: string;
  referenceMonth?: string;
  notes?: string;
}
interface Envio {
  corpo: Corpo;
  chave: string | undefined;
}

interface Opcoes {
  carteiras?: object[];
  /** Respostas de `GET /entries/suggestions` por texto digitado. */
  sugestoes?: Record<string, object[]>;
  /** Quantas primeiras criações devem falhar por falta de conexão. */
  falhasDeRede?: number;
}

/** API simulada: carteiras, sugestões e criação de lançamentos (guarda o que foi enviado). */
async function simularApi(page: Page, opcoes: Opcoes = {}) {
  const envios: Envio[] = [];
  const carteiras = [...(opcoes.carteiras ?? [azul, verde])];
  let falhas = opcoes.falhasDeRede ?? 0;

  await comecarEmSaida(page);
  await page.clock.setFixedTime(HOJE);
  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', async (route: Route) => {
    const req = route.request();
    if (req.method() === 'POST') {
      const corpo = req.postDataJSON();
      const nova = {
        ...azul,
        id: `w-${carteiras.length + 1}`,
        name: corpo.name,
        initialBalanceDate: corpo.initialBalanceDate,
        isPrimary: carteiras.length === 0,
      };
      carteiras.push(nova);
      return route.fulfill({ status: 201, json: nova });
    }
    return route.fulfill({ json: carteiras });
  });
  await page.route('**/api/v1/entries/suggestions**', (route) => {
    const q = new URL(route.request().url()).searchParams.get('q') ?? '';
    return route.fulfill({ json: opcoes.sugestoes?.[q.toLowerCase()] ?? [] });
  });
  await page.route('**/api/v1/entries', async (route) => {
    const req = route.request();
    if (req.method() !== 'POST') return route.fulfill({ json: [] });
    const corpo = req.postDataJSON() as Corpo;
    if (falhas > 0) {
      falhas--;
      return route.abort('connectionrefused');
    }
    envios.push({ corpo, chave: req.headers()['idempotency-key'] });
    return route.fulfill({
      status: 201,
      json: {
        id: `e${envios.length}`,
        kind: corpo.kind,
        description: corpo.description,
        amountCents: 4499,
        date: corpo.date,
        referenceMonth: corpo.referenceMonth ?? corpo.date.slice(0, 7),
        status: corpo.status ?? 'settled',
        walletId: corpo.walletId,
        createdAt: '2026-03-10T15:00:00Z',
        version: 1,
      },
    });
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
const carteira = (page: Page) => page.getByLabel('Carteira', { exact: true });
const situacao = (page: Page, nome: string) => page.getByRole('radio', { name: nome, exact: true });

test.describe('formulário rápido de lançamento (US2) — API simulada', () => {
  test('"+" abre o formulário: tela cheia no celular, diálogo no computador (FR-008)', async ({
    page,
  }, info) => {
    await simularApi(page);
    await abrir(page);

    const caixa = await page.getByRole('dialog').boundingBox();
    const viewport = page.viewportSize()!;
    if (info.project.name === 'mobile') {
      expect(caixa!.width).toBe(viewport.width);
      expect(caixa!.height).toBe(viewport.height);
    } else {
      expect(caixa!.width).toBeLessThan(viewport.width / 2);
    }
    await expect(valor(page)).toBeFocused();
    await expect(situacao(page, 'Saída')).toHaveAttribute('aria-checked', 'true');
    await page.getByRole('button', { name: 'Fechar' }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  test('US2.1: Saída de 44,99 "Tim" usa data de hoje, carteira principal e situação padrão', async ({
    page,
  }) => {
    const envios = await simularApi(page);
    await abrir(page);

    await valor(page).fill('44,99');
    await descricao(page).fill('Tim');
    await expect(page.getByLabel('Data')).toHaveValue('2026-03-10');
    await expect(carteira(page)).toHaveValue('w-azul');
    await expect(situacao(page, 'Paga')).toHaveAttribute('aria-checked', 'true');
    await page.getByTestId('salvar').click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios).toHaveLength(1);
    expect(envios[0].corpo).toEqual({
      kind: 'expense',
      description: 'Tim',
      amount: '44,99',
      date: '2026-03-10',
      walletId: 'w-azul',
    });
    expect(envios[0].chave).toMatch(/^[0-9a-f-]{36}$/);
  });

  test('US2.2: a conta 29+73,2 mostra "= R$ 102,20" e vai como digitada', async ({ page }) => {
    const envios = await simularApi(page);
    await abrir(page);

    await valor(page).fill('29+73,2');
    await expect(page.getByText('= R$ 102,20')).toBeVisible();
    await descricao(page).fill('Mercado');
    await page.getByTestId('salvar').click();

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo.amount).toBe('29+73,2');
  });

  test('US2.3: data futura vira Prevista; situação escolhida à mão deixa de seguir a data (FR-012)', async ({
    page,
  }) => {
    const envios = await simularApi(page);
    await abrir(page);

    await page.getByLabel('Data').fill('2026-03-25');
    await expect(situacao(page, 'Prevista')).toHaveAttribute('aria-checked', 'true');
    await page.getByLabel('Data').fill('2026-03-10');
    await expect(situacao(page, 'Paga')).toHaveAttribute('aria-checked', 'true');

    await situacao(page, 'Prevista').click();
    await page.getByLabel('Data').fill('2026-03-05');
    await expect(situacao(page, 'Prevista')).toHaveAttribute('aria-checked', 'true');

    await valor(page).fill('10');
    await descricao(page).fill('Luz');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo.status).toBe('planned');
  });

  test('US2.3: sem mexer na situação, o app não envia `status` (o servidor decide)', async ({
    page,
  }) => {
    const envios = await simularApi(page);
    await abrir(page);

    await valor(page).fill('18,27');
    await descricao(page).fill('Duda');
    await page.getByLabel('Data').fill('2026-03-25');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo).not.toHaveProperty('status');
  });

  test('US2.4: sugestão aplica a carteira (se não alterada à mão) e nunca o valor', async ({
    page,
  }) => {
    await simularApi(page, {
      sugestoes: { ti: [{ description: 'Tim', uses: 3, walletId: 'w-verde' }] },
    });
    await abrir(page);

    await descricao(page).fill('Ti');
    await page.getByTestId('sugestao').filter({ hasText: 'Tim' }).click();
    await expect(descricao(page)).toHaveValue('Tim');
    await expect(carteira(page)).toHaveValue('w-verde');
    await expect(valor(page)).toHaveValue('');
    await expect(page.getByTestId('sugestao')).toHaveCount(0);
  });

  test('US2.4: carteira escolhida à mão não é trocada pela sugestão', async ({ page }) => {
    await simularApi(page, {
      sugestoes: { ti: [{ description: 'Tim', uses: 3, walletId: 'w-verde' }] },
    });
    await abrir(page);

    await carteira(page).selectOption('w-azul');
    await descricao(page).fill('Ti');
    await page.getByTestId('sugestao').click();
    await expect(carteira(page)).toHaveValue('w-azul');
  });

  test('SC-001: com sugestão, lança em no máximo 4 toques', async ({ page }) => {
    const envios = await simularApi(page, {
      sugestoes: { ti: [{ description: 'Tim', uses: 3, walletId: 'w-verde' }] },
    });
    await page.goto('/');
    let toques = 0;
    const tocar = async (alvo: ReturnType<Page['getByTestId']>) => {
      toques++;
      await alvo.click();
    };

    await tocar(page.getByTestId('lancar'));
    await valor(page).fill('44,99'); // digitação, não é toque num botão
    await descricao(page).fill('Ti');
    await tocar(page.getByTestId('sugestao'));
    await tocar(page.getByTestId('salvar'));

    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(toques).toBeLessThanOrEqual(4);
    expect(envios[0].corpo).toMatchObject({ description: 'Tim', walletId: 'w-verde' });
  });

  test('US2.5: "Salvar e lançar outro" reabre limpo mantendo tipo, data e carteira', async ({
    page,
  }) => {
    const envios = await simularApi(page);
    await abrir(page);

    await situacao(page, 'Entrada').click();
    await carteira(page).selectOption('w-verde');
    await page.getByLabel('Data').fill('2026-03-08');
    await valor(page).fill('1500');
    await descricao(page).fill('Salário');
    await page.getByTestId('salvar-outro').click();

    await expect(valor(page)).toHaveValue('');
    await expect(valor(page)).toBeFocused();
    await expect(descricao(page)).toHaveValue('');
    await expect(situacao(page, 'Entrada')).toHaveAttribute('aria-checked', 'true');
    await expect(carteira(page)).toHaveValue('w-verde');
    await expect(page.getByLabel('Data')).toHaveValue('2026-03-08');

    await valor(page).fill('20');
    await descricao(page).fill('Duda');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);

    expect(envios).toHaveLength(2);
    expect(envios[0].chave).not.toBe(envios[1].chave);
    expect(envios[1].corpo).toMatchObject({
      kind: 'income',
      walletId: 'w-verde',
      date: '2026-03-08',
      description: 'Duda',
    });
  });

  test('US2.6: valor 0 ou -5 não salva e mostra "Informe um valor maior que zero"', async ({
    page,
  }) => {
    const envios = await simularApi(page);
    await abrir(page);
    await descricao(page).fill('Teste');

    for (const texto of ['0', '-5']) {
      await valor(page).fill(texto);
      await page.getByTestId('salvar').click();
      await expect(page.getByText('Informe um valor maior que zero')).toBeVisible();
      await expect(page.getByRole('dialog')).toBeVisible();
    }
    expect(envios).toHaveLength(0);
  });

  test('descrição vazia não salva', async ({ page }) => {
    const envios = await simularApi(page);
    await abrir(page);
    await valor(page).fill('10');
    await page.getByTestId('salvar').click();
    await expect(page.getByTestId('erro-descricao')).toHaveText('Informe a descrição.');
    expect(envios).toHaveLength(0);
  });

  test('US2.7: sem conexão avisa, mantém o formulário e reenvia com a mesma chave', async ({
    page,
  }) => {
    const envios = await simularApi(page, { falhasDeRede: 1 });
    await abrir(page);

    await valor(page).fill('44,99');
    await descricao(page).fill('Tim');
    await page.getByTestId('salvar').click();

    await expect(
      page.getByText('Sem conexão. O lançamento não foi salvo; tente de novo.'),
    ).toBeVisible();
    await expect(page.getByRole('dialog')).toBeVisible();
    await expect(valor(page)).toHaveValue('44,99');
    await expect(descricao(page)).toHaveValue('Tim');
    expect(envios).toHaveLength(0);

    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios).toHaveLength(1);
  });

  test('Idempotency-Key: reenvio idêntico reaproveita a chave; corpo alterado gera outra', async ({
    page,
  }) => {
    const chaves: (string | undefined)[] = [];
    await simularApi(page);
    await page.route('**/api/v1/entries', (route) => {
      if (route.request().method() !== 'POST') return route.fallback();
      chaves.push(route.request().headers()['idempotency-key']);
      return route.abort('connectionrefused');
    });
    await abrir(page);

    await valor(page).fill('10');
    await descricao(page).fill('Luz');
    await page.getByTestId('salvar').click();
    await expect(page.getByText(/Sem conexão/)).toBeVisible();
    await page.getByTestId('salvar').click();
    await expect.poll(() => chaves.length).toBe(2);
    expect(chaves[0]).toBe(chaves[1]);

    await valor(page).fill('11');
    await page.getByTestId('salvar').click();
    await expect.poll(() => chaves.length).toBe(3);
    expect(chaves[2]).not.toBe(chaves[0]);
  });

  test('sem carteira: "Criar carteira" cria ali mesmo sem perder o que foi digitado', async ({
    page,
  }) => {
    const envios = await simularApi(page, { carteiras: [] });
    await abrir(page);

    await valor(page).fill('44,99');
    await descricao(page).fill('Tim');
    await expect(carteira(page)).toHaveCount(0);
    await page.getByRole('button', { name: 'Criar carteira' }).click();
    await page.getByLabel('Nome da carteira').fill('Azul');
    await page.getByLabel('Saldo inicial').fill('500');
    await page.getByRole('button', { name: 'Criar', exact: true }).click();

    await expect(carteira(page)).toHaveValue('w-1');
    await expect(valor(page)).toHaveValue('44,99');
    await expect(descricao(page)).toHaveValue('Tim');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios[0].corpo).toMatchObject({ walletId: 'w-1', description: 'Tim' });
  });

  test('data anterior ao saldo inicial da carteira: aviso não bloqueante', async ({ page }) => {
    const envios = await simularApi(page);
    await abrir(page);

    await page.getByLabel('Data').fill('2026-02-20');
    await expect(page.getByTestId('aviso-data-anterior')).toContainText(
      'Esta data é anterior ao saldo inicial da carteira. O saldo inicial já deveria incluir este valor.',
    );
    await valor(page).fill('10');
    await descricao(page).fill('Antiga');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(envios).toHaveLength(1);

    await abrir(page);
    await page.getByLabel('Data').fill('2026-03-02');
    await expect(page.getByTestId('aviso-data-anterior')).toHaveCount(0);
  });

  test('"Mais opções": Mês de referência e Observação seguem na criação', async ({ page }) => {
    const envios = await simularApi(page);
    await abrir(page);

    await expect(page.getByLabel('Observação')).toHaveCount(0);
    await page.getByRole('button', { name: 'Mais opções' }).click();
    await expect(page.getByLabel('Mês de referência')).toHaveValue('2026-03');
    await page.getByLabel('Mês de referência').fill('2026-04');
    await page.getByLabel('Observação').fill('adiantado');
    await valor(page).fill('1500');
    await descricao(page).fill('Salário');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);

    expect(envios[0].corpo).toMatchObject({ referenceMonth: '2026-04', notes: 'adiantado' });
  });

  test('o último tipo e a última carteira ficam no aparelho (FR-009, FR-010)', async ({ page }) => {
    await simularApi(page);
    await abrir(page);
    await situacao(page, 'Entrada').click();
    await carteira(page).selectOption('w-verde');
    await valor(page).fill('20');
    await descricao(page).fill('Duda');
    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await page.reload();
    await page.getByTestId('lancar').click();
    await expect(situacao(page, 'Entrada')).toHaveAttribute('aria-checked', 'true');
    await expect(carteira(page)).toHaveValue('w-verde');
    // no tipo ainda sem uso, vale a principal
    await situacao(page, 'Saída').click();
    await expect(carteira(page)).toHaveValue('w-azul');
  });

  test('rascunho: fechar sem salvar guarda o que foi digitado; salvar apaga', async ({ page }) => {
    await simularApi(page);
    await abrir(page);
    await valor(page).fill('30');
    await descricao(page).fill('Léo Casa');
    await page.getByRole('button', { name: 'Fechar' }).click();

    await page.reload();
    await page.getByTestId('lancar').click();
    await expect(valor(page)).toHaveValue('30');
    await expect(descricao(page)).toHaveValue('Léo Casa');

    await page.getByTestId('salvar').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await page.reload();
    await page.getByTestId('lancar').click();
    await expect(valor(page)).toHaveValue('');
  });
});

// Contra a stack real (E2E_BASE_URL): o servidor aplica as regras (RT-05, FR-011, FR-012, RT-04).
test.describe('formulário rápido de lançamento (US2) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  async function criarConta(page: Page) {
    const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
    await comecarEmSaida(page);
    await page.goto('/criar-conta');
    await page.getByLabel('Nome', { exact: true }).fill('Lia Souza');
    await page.getByLabel('E-mail').fill(email);
    await page.getByLabel('Senha', { exact: true }).fill('senha-teste-123');
    await page.getByRole('button', { name: 'Criar conta' }).click();
    await dispensarGuiaSeAberto(page);
  }

  const criacao = (page: Page) =>
    page.waitForResponse(
      (r) => r.url().endsWith('/api/v1/entries') && r.request().method() === 'POST',
    );

  test('cria a carteira ali, lança a conta 29+73,2, uma prevista e usa a sugestão do servidor', async ({
    page,
  }) => {
    await criarConta(page);
    await page.getByTestId('lancar').click();

    await page.getByRole('button', { name: 'Criar carteira' }).click();
    await page.getByLabel('Nome da carteira').fill('Azul');
    await page.getByLabel('Saldo inicial').fill('500');
    await page.getByRole('button', { name: 'Criar', exact: true }).click();
    await expect(carteira(page)).toHaveValue(/.+/);

    await valor(page).fill('29+73,2');
    await descricao(page).fill('Tim');
    const primeira = criacao(page);
    await page.getByTestId('salvar-outro').click();
    const lancamento = await (await primeira).json();
    expect(lancamento).toMatchObject({
      kind: 'expense',
      description: 'Tim',
      amountCents: 10220,
      status: 'settled',
    });
    expect(lancamento.referenceMonth).toBe(lancamento.date.slice(0, 7));

    // data futura sem mexer na situação → o servidor grava Prevista (US2.3)
    const futura = new Date(Date.now() + 40 * 86_400_000).toISOString().slice(0, 10);
    await page.getByLabel('Data').fill(futura);
    await expect(situacao(page, 'Prevista')).toHaveAttribute('aria-checked', 'true');
    await valor(page).fill('10');
    await descricao(page).fill('Luz');
    const segunda = criacao(page);
    await page.getByTestId('salvar-outro').click();
    expect(await (await segunda).json()).toMatchObject({ status: 'planned', amountCents: 1000 });

    // sugestão vinda do servidor: aplica a carteira, nunca o valor (US2.4)
    await descricao(page).fill('Ti');
    await page.getByTestId('sugestao').filter({ hasText: 'Tim' }).click();
    await expect(descricao(page)).toHaveValue('Tim');
    await expect(valor(page)).toHaveValue('');
  });
});
