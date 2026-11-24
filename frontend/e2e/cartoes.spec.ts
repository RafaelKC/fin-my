import { expect, test, type Page } from '@playwright/test';
import { dispensarGuiaSeAberto } from './espaco-novo';
import { irPara } from './navegacao';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};

interface Cartao {
  id: string;
  name: string;
  closingDay: number;
  dueDay: number;
  paymentWalletId: string;
  color: string;
  limitCents: number | null;
  archived: boolean;
  hasPurchases: boolean;
  version: number;
  availableLimitCents: number | null;
}

const problema = (status: number, code: string, detail: string) => ({
  status,
  contentType: 'application/problem+json',
  json: { type: 'about:blank', title: code, status, code, detail },
});

const carteira = (id: string, name: string, isPrimary: boolean) => ({
  id,
  name,
  type: 'checking',
  color: '#1E88E5',
  initialBalanceCents: 0,
  initialBalanceDate: '2026-03-01',
  isPrimary,
  archived: false,
  currentBalanceCents: 0,
  hasEntries: false,
  version: 1,
});

const semAcento = (t: string) => t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

function fatura(id: string, mes: string, total = 0, extra: object = {}) {
  return {
    creditCardId: id,
    dueMonth: mes,
    closingDate: `${mes}-18`,
    dueDate: `${mes}-25`,
    id: null,
    status: 'no_amount_due',
    totalCents: total,
    paidCents: 0,
    remainingCents: total,
    ...extra,
  };
}

/** API de cartões e carteiras em memória (nome único, arquivar, excluir só sem compras). */
async function simularApi(page: Page, iniciais: Partial<Cartao>[] = []) {
  let seq = 0;
  const lista: Cartao[] = [];
  const abertas = new Map<string, object[]>();
  const criar = (c: Partial<Cartao>): Cartao => {
    const novo: Cartao = {
      id: `c${++seq}`,
      name: 'Cartão',
      closingDay: 18,
      dueDay: 25,
      paymentWalletId: 'w-azul',
      color: '#1E88E5',
      limitCents: null,
      archived: false,
      hasPurchases: false,
      version: 1,
      availableLimitCents: null,
      ...c,
    };
    lista.push(novo);
    return novo;
  };
  iniciais.forEach(criar);
  const repetido = (nome: string, ignorar?: string) =>
    lista.some((c) => c.id !== ignorar && semAcento(c.name) === semAcento(nome));
  const visao = (c: Cartao) => ({
    ...c,
    currentInvoice: fatura(c.id, '2026-03'),
    nextInvoice: fatura(c.id, '2026-04'),
    openPastInvoices: abertas.get(c.id) ?? [],
  });
  const dados = (corpo: Record<string, string | number | null>) => {
    const texto = String(corpo['limit'] ?? '').replace(',', '.');
    const limitCents = texto ? Math.round(Number(texto) * 100) : null;
    return { ...corpo, limit: undefined, limitCents, availableLimitCents: limitCents };
  };

  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', (route) =>
    route.fulfill({
      json: [carteira('w-azul', 'Azul', true), carteira('w-verde', 'Verde', false)],
    }),
  );
  await page.route('**/api/v1/credit-cards**', async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const [id, acao] = url.pathname.replace('/api/v1/credit-cards', '').split('/').filter(Boolean);
    const alvo = lista.find((c) => c.id === id);
    const corpo = req.postDataJSON() ?? {};

    if (!id) {
      if (req.method() === 'GET') {
        const arq = url.searchParams.get('archived');
        return route.fulfill({
          json: lista.filter((c) => arq === 'all' || c.archived === (arq === 'true')).map(visao),
        });
      }
      if (repetido(corpo.name))
        return route.fulfill(
          problema(409, 'credit_card.name_taken', 'Já existe um cartão com esse nome.'),
        );
      return route.fulfill({ status: 201, json: criar(dados(corpo) as Partial<Cartao>) });
    }
    if (!alvo) return route.fulfill(problema(404, 'not_found', 'Cartão não encontrado.'));

    if (!acao && req.method() === 'GET') return route.fulfill({ json: visao(alvo) });
    if (!acao && req.method() === 'PUT') {
      if (repetido(corpo.name, alvo.id))
        return route.fulfill(
          problema(409, 'credit_card.name_taken', 'Já existe um cartão com esse nome.'),
        );
      Object.assign(alvo, dados(corpo), { version: alvo.version + 1 });
      return route.fulfill({ json: alvo });
    }
    if (!acao && req.method() === 'DELETE') {
      if (alvo.hasPurchases)
        return route.fulfill(
          problema(409, 'credit_card.has_purchases_archive_instead', 'Tem compras.'),
        );
      lista.splice(lista.indexOf(alvo), 1);
      return route.fulfill({ status: 204 });
    }
    if (acao === 'archive') {
      alvo.archived = true;
      return route.fulfill({ json: alvo });
    }
    if (acao === 'unarchive') {
      alvo.archived = false;
      return route.fulfill({ json: alvo });
    }
    return route.fulfill(problema(404, 'not_found', 'Não encontrada.'));
  });
  return { lista, abertas };
}

test.describe('cartões (US1, US6) — API simulada', () => {
  test('cadastra "Azul" e o vê com a fatura atual zerada; "AZUL" é recusado (US1.1, US1.2)', async ({
    page,
  }) => {
    await simularApi(page);
    await page.goto('/cartoes');
    await expect(page.getByTestId('lista-vazia')).toBeVisible();

    await page.getByRole('link', { name: 'Novo cartão' }).click();
    await expect(page).toHaveURL(/\/cartoes\/novo$/);
    await expect(page.getByRole('radio')).toHaveCount(10);
    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Dia de fechamento').fill('18');
    await page.getByLabel('Dia de vencimento').fill('25');
    await page.getByLabel('Carteira de pagamento').selectOption({ label: 'Azul' });
    await page.getByRole('radio', { name: 'Verde' }).click();
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page).toHaveURL(/\/cartoes$/);
    const linha = page.getByTestId('cartao').filter({ hasText: 'Azul' });
    await expect(linha.getByTestId('fatura-atual')).toContainText('R$ 0,00');
    await expect(linha.getByTestId('fatura-atual')).toContainText('fecha em 18/03');

    await page.getByRole('link', { name: 'Novo cartão' }).click();
    await page.getByLabel('Nome', { exact: true }).fill('AZUL');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByText('Já existe um cartão com esse nome.')).toBeVisible();
    await expect(page).toHaveURL(/\/cartoes\/novo$/);
  });

  test('valida nome e dias antes de chamar a API', async ({ page }) => {
    await simularApi(page);
    await page.goto('/cartoes/novo');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByText('Informe o nome do cartão.')).toBeVisible();
    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Dia de vencimento').fill('32');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByText('Informe um dia de 1 a 31.')).toBeVisible();
    await expect(page).toHaveURL(/\/cartoes\/novo$/);
  });

  test('edita pelo "Editar" da lista (dia de vencimento e limite) (US1)', async ({ page }) => {
    await simularApi(page, [{ name: 'Azul' }]);
    await page.goto('/cartoes');
    await page
      .getByTestId('cartao')
      .getByRole('link', { name: /Editar/ })
      .click();
    await expect(page).toHaveURL(/\/cartoes\/c1\/editar$/);
    await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('Azul');
    await page.getByLabel('Dia de vencimento').fill('28');
    await page.getByLabel('Limite (opcional)').fill('3000');
    await expect(page.getByTestId('limite-previa')).toHaveText('= R$ 3.000,00');
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page).toHaveURL(/\/cartoes$/);
    await expect(page.getByTestId('limite-disponivel')).toContainText('R$ 3.000,00');
  });

  test('cartão com compras só arquiva; sem compras exclui com confirmação (US1.3)', async ({
    page,
  }) => {
    await simularApi(page, [
      { name: 'Azul', hasPurchases: true },
      { name: 'Verde', color: '#43A047' },
    ]);
    await page.goto('/cartoes');

    await page
      .getByTestId('cartao')
      .filter({ hasText: 'Azul' })
      .getByRole('link', { name: /Editar/ })
      .click();
    await expect(page.getByRole('button', { name: 'Excluir' })).toHaveCount(0);
    await expect(page.getByTestId('so-arquivar')).toBeVisible();
    await page.getByRole('button', { name: 'Arquivar' }).click();
    await expect(page).toHaveURL(/\/cartoes$/);
    await expect(page.getByTestId('cartao').filter({ hasText: 'Azul' })).toHaveCount(0);

    await page.getByTestId('filtro-arquivados').check();
    await expect(page.getByTestId('cartao').filter({ hasText: 'Azul' })).toContainText('Arquivado');
    await page.getByTestId('filtro-arquivados').uncheck();

    await page
      .getByTestId('cartao')
      .getByRole('link', { name: /Editar/ })
      .click();
    await page.getByRole('button', { name: 'Excluir' }).click();
    const dialogo = page.getByRole('dialog');
    await dialogo.getByRole('button', { name: 'Cancelar' }).click();
    await expect(dialogo).toHaveCount(0);
    await page.getByRole('button', { name: 'Excluir' }).click();
    await page.getByRole('dialog').getByTestId('confirmar').click();
    await expect(page).toHaveURL(/\/cartoes$/);
    await expect(page.getByTestId('lista-vazia')).toBeVisible();
  });

  test('limite disponível R$ 2.333,34 com limite e faturas futuras; sem limite não aparece (US6.1, US6.3)', async ({
    page,
  }) => {
    await simularApi(page, [
      { name: 'Azul', limitCents: 300000, availableLimitCents: 233334 },
      { name: 'Verde', color: '#43A047' },
    ]);
    await page.goto('/cartoes');
    const azul = page.getByTestId('cartao').filter({ hasText: 'Azul' });
    await expect(azul.getByTestId('limite-disponivel')).toContainText('R$ 2.333,34');
    const verde = page.getByTestId('cartao').filter({ hasText: 'Verde' });
    await expect(verde.getByTestId('limite-disponivel')).toHaveCount(0);
  });

  test('faturas em aberto aparecem; a vencida em vermelho (FR-025)', async ({ page }) => {
    const { abertas } = await simularApi(page, [{ name: 'Azul' }]);
    abertas.set('c1', [
      fatura('c1', '2026-01', 15000, { status: 'overdue' }),
      fatura('c1', '2026-02', 3000, { status: 'closed' }),
    ]);
    await page.goto('/cartoes');
    const itens = page.getByTestId('fatura-aberta');
    await expect(itens).toHaveCount(2);
    await expect(itens.nth(0)).toContainText('R$ 150,00 em aberto');
    await expect(itens.nth(0)).toHaveClass(/vencida/);
    await expect(itens.nth(1)).not.toHaveClass(/vencida/);
  });

  test('"Cartões" abre a lista pela navegação da casca', async ({ page }) => {
    await simularApi(page);
    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    await irPara(page, 'Cartões');
    await expect(page).toHaveURL(/\/cartoes$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Cartões' })).toBeVisible();
  });
});

// Contra a stack real (E2E_BASE_URL). Depende da API de cartões (T004): enquanto
// `/api/v1/credit-cards` não existir (404), os testes são pulados.
test.describe('cartões (US1, US6) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  test.beforeEach(async ({ request }) => {
    const resposta = await request.get('/api/v1/credit-cards');
    test.skip(resposta.status() === 404, 'API de cartões (T004) ainda não está na stack');
  });

  async function criarContaComCarteira(page: Page) {
    const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
    await page.goto('/criar-conta');
    await page.getByLabel('Nome', { exact: true }).fill('Lia Souza');
    await page.getByLabel('E-mail').fill(email);
    await page.getByLabel('Senha', { exact: true }).fill('senha-teste-123');
    await page.getByRole('button', { name: 'Criar conta' }).click();
    await dispensarGuiaSeAberto(page);

    await page.goto('/carteiras/nova');
    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Saldo inicial', { exact: true }).fill('500,00');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page).toHaveURL(/\/carteiras$/);
  }

  async function novoCartao(page: Page, nome: string, limite = '') {
    await page.goto('/cartoes/novo');
    await page.getByLabel('Nome', { exact: true }).fill(nome);
    await page.getByLabel('Dia de fechamento').fill('18');
    await page.getByLabel('Dia de vencimento').fill('25');
    await page.getByLabel('Limite (opcional)').fill(limite);
    await page.getByRole('button', { name: 'Salvar' }).click();
  }

  test('US1.1 e US1.2: cadastra Azul (fatura atual R$ 0,00) e recusa "AZUL"', async ({ page }) => {
    await criarContaComCarteira(page);
    await novoCartao(page, 'Azul');
    await expect(page).toHaveURL(/\/cartoes$/);
    const linha = page.getByTestId('cartao').filter({ hasText: 'Azul' });
    await expect(linha.getByTestId('fatura-atual')).toContainText('R$ 0,00');
    await expect(linha.getByTestId('fatura-atual')).toContainText('fecha em 18/');

    await novoCartao(page, 'AZUL');
    await expect(page.getByText('Já existe um cartão com esse nome.')).toBeVisible();
  });

  test('US6.1 e US6.3: limite disponível só aparece em cartão com limite', async ({ page }) => {
    await criarContaComCarteira(page);
    await novoCartao(page, 'Azul', '3000');
    await novoCartao(page, 'Verde');
    await expect(page).toHaveURL(/\/cartoes$/);
    await expect(
      page.getByTestId('cartao').filter({ hasText: 'Azul' }).getByTestId('limite-disponivel'),
    ).toContainText('R$ 3.000,00');
    await expect(
      page.getByTestId('cartao').filter({ hasText: 'Verde' }).getByTestId('limite-disponivel'),
    ).toHaveCount(0);
  });

  test('US1.3: cartão sem compras pode ser excluído; arquivar e desarquivar', async ({ page }) => {
    await criarContaComCarteira(page);
    await novoCartao(page, 'Azul');
    await page
      .getByTestId('cartao')
      .getByRole('link', { name: /Editar/ })
      .click();
    await page.getByRole('button', { name: 'Arquivar' }).click();
    await expect(page).toHaveURL(/\/cartoes$/);
    await page.getByTestId('filtro-arquivados').check();
    await page
      .getByTestId('cartao')
      .getByRole('link', { name: /Editar/ })
      .click();
    await page.getByRole('button', { name: 'Desarquivar' }).click();
    await page.getByRole('button', { name: 'Excluir' }).click();
    await page.getByRole('dialog').getByTestId('confirmar').click();
    await expect(page).toHaveURL(/\/cartoes$/);
    await expect(page.getByTestId('lista-vazia')).toBeVisible();
  });
});
