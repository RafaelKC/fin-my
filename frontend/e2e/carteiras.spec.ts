import { expect, test, type Page } from '@playwright/test';
import { dispensarGuiaSeAberto } from './espaco-novo';
import { irPara } from './navegacao';

const sessao = {
  accessToken: 'token',
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'u1', name: 'Lia', email: 'lia@example.com' },
  tenant: { id: 't1', name: 'Espaço da Lia', role: 'owner' },
};

interface Carteira {
  id: string;
  name: string;
  type: string;
  color: string;
  initialBalanceCents: number;
  initialBalanceDate: string;
  isPrimary: boolean;
  archived: boolean;
  currentBalanceCents: number;
  hasEntries: boolean;
  version: number;
}

const problema = (status: number, code: string, detail: string) => ({
  status,
  contentType: 'application/problem+json',
  json: { type: 'about:blank', title: code, status, code, detail },
});

function centavos(texto: string): number {
  // só somas e subtraições simples (suficiente para a simulação)
  const termos =
    texto
      .replace(/\s/g, '')
      .replace(/,/g, '.')
      .match(/[+-]?[\d.]+/g) ?? [];
  return Math.round(termos.reduce((total, t) => total + Number(t), 0) * 100);
}

/** API de carteiras em memória (regras do contrato: nome único, principal, arquivar, excluir). */
async function simularApi(page: Page, iniciais: Partial<Carteira>[] = []) {
  let seq = 0;
  const lista: Carteira[] = [];
  const criar = (c: Partial<Carteira>): Carteira => {
    const nova: Carteira = {
      id: `w${++seq}`,
      name: 'Carteira',
      type: 'checking',
      color: '#1E88E5',
      initialBalanceCents: 0,
      initialBalanceDate: '2026-03-01',
      isPrimary: false,
      archived: false,
      currentBalanceCents: c.initialBalanceCents ?? 0,
      hasEntries: false,
      version: 1,
      ...c,
    };
    lista.push(nova);
    return nova;
  };
  iniciais.forEach(criar);
  const ativas = () => lista.filter((w) => !w.archived);
  const repetido = (nome: string, ignorar?: string) =>
    lista.some((w) => w.id !== ignorar && w.name.toLowerCase() === nome.toLowerCase());

  await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
  await page.route('**/api/v1/wallets**', async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const partes = url.pathname.replace('/api/v1/wallets', '').split('/').filter(Boolean);
    const [id, acao] = partes;
    const alvo = lista.find((w) => w.id === id);
    const corpo = req.postDataJSON() ?? {};

    if (!id) {
      if (req.method() === 'GET') {
        const arq = url.searchParams.get('archived');
        return route.fulfill({
          json: lista.filter((w) => arq === 'all' || w.archived === (arq === 'true')),
        });
      }
      if (repetido(corpo.name))
        return route.fulfill(
          problema(409, 'wallet.name_taken', 'Já existe uma carteira com esse nome.'),
        );
      const c = criar({
        ...corpo,
        initialBalanceCents: centavos(corpo.initialBalance),
        currentBalanceCents: centavos(corpo.initialBalance),
        isPrimary: ativas().length === 0,
      });
      return route.fulfill({ status: 201, json: c });
    }
    if (!alvo) return route.fulfill(problema(404, 'not_found', 'Não encontrada.'));

    if (!acao && req.method() === 'GET') return route.fulfill({ json: alvo });
    if (!acao && req.method() === 'PUT') {
      if (repetido(corpo.name, alvo.id))
        return route.fulfill(
          problema(409, 'wallet.name_taken', 'Já existe uma carteira com esse nome.'),
        );
      Object.assign(alvo, corpo, {
        initialBalanceCents: centavos(corpo.initialBalance),
        version: alvo.version + 1,
      });
      return route.fulfill({ json: alvo });
    }
    if (!acao && req.method() === 'DELETE') {
      if (alvo.hasEntries)
        return route.fulfill(
          problema(409, 'wallet.has_entries_archive_instead', 'Esta carteira tem lançamentos.'),
        );
      lista.splice(lista.indexOf(alvo), 1);
      const nova = lista.find((w) => w.id === url.searchParams.get('newPrimaryWalletId'));
      if (alvo.isPrimary && nova) nova.isPrimary = true;
      return route.fulfill({ status: 204 });
    }
    if (acao === 'make-primary') {
      lista.forEach((w) => (w.isPrimary = w.id === alvo.id));
      return route.fulfill({ json: alvo });
    }
    if (acao === 'archive') {
      if (alvo.isPrimary) {
        if (ativas().length === 1)
          return route.fulfill(problema(422, 'wallet.last_active_cannot_be_archived', 'Única.'));
        const nova = lista.find((w) => w.id === corpo.newPrimaryWalletId && !w.archived);
        if (!nova)
          return route.fulfill(problema(422, 'wallet.new_primary_required', 'Escolha outra.'));
        nova.isPrimary = true;
        alvo.isPrimary = false;
      }
      alvo.archived = true;
      return route.fulfill({ json: alvo });
    }
    if (acao === 'unarchive') {
      alvo.archived = false;
      return route.fulfill({ json: alvo });
    }
    return route.fulfill(problema(404, 'not_found', 'Não encontrada.'));
  });
  return lista;
}

test.describe('carteiras (US1) — API simulada', () => {
  test('cadastra "Azul" com saldo 500, vira principal; "azul" é recusada (US1.1, US1.2)', async ({
    page,
  }) => {
    await simularApi(page);
    await page.goto('/carteiras');
    await expect(page.getByTestId('lista-vazia')).toBeVisible();

    await page.getByRole('link', { name: 'Nova carteira' }).click();
    await expect(page).toHaveURL(/\/carteiras\/nova$/);
    await expect(page.getByRole('radiogroup', { name: 'Cor' }).getByRole('radio')).toHaveCount(10);
    await page.getByLabel('Nome', { exact: true }).fill('Azul');
    await page.getByLabel('Saldo inicial', { exact: true }).fill('400+100');
    await expect(page.getByTestId('saldo-previa')).toHaveText('= R$ 500,00');
    await page.getByLabel('Data do saldo inicial').fill('2026-03-01');
    await page.getByRole('radio', { name: 'Verde' }).click();
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page).toHaveURL(/\/carteiras$/);
    const linha = page.getByTestId('carteira').filter({ hasText: 'Azul' });
    await expect(linha).toContainText('R$ 500,00');
    await expect(linha.getByTestId('principal')).toBeVisible();

    await page.getByRole('link', { name: 'Nova carteira' }).click();
    await page.getByLabel('Nome', { exact: true }).fill('azul');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByText('Já existe uma carteira com esse nome.')).toBeVisible();
    await expect(page).toHaveURL(/\/carteiras\/nova$/);
  });

  test('arquivar a principal pede a nova principal; filtro Arquivadas (US1.4)', async ({
    page,
  }) => {
    await simularApi(page, [
      { name: 'Azul', isPrimary: true, initialBalanceCents: 50000, currentBalanceCents: 50000 },
      { name: 'Verde', color: '#43A047', initialBalanceCents: 1000, currentBalanceCents: 1000 },
    ]);
    await page.goto('/carteiras');
    await page.getByTestId('carteira').filter({ hasText: 'Azul' }).click();
    await page.getByRole('button', { name: 'Arquivar' }).click();

    const dialogo = page.getByRole('dialog');
    await expect(dialogo).toBeVisible();
    await expect(dialogo.getByTestId('confirmar')).toBeDisabled();
    await dialogo.getByLabel('Nova carteira principal').selectOption({ label: 'Verde' });
    await dialogo.getByTestId('confirmar').click();

    await expect(page).toHaveURL(/\/carteiras$/);
    const verde = page.getByTestId('carteira').filter({ hasText: 'Verde' });
    await expect(verde.getByTestId('principal')).toBeVisible();
    await expect(page.getByTestId('carteira').filter({ hasText: 'Azul' })).toHaveCount(0);

    await page.getByTestId('filtro-arquivadas').check();
    await expect(page.getByTestId('carteira').filter({ hasText: 'Azul' })).toContainText(
      'Arquivada',
    );
  });

  test('a única ativa não pode ser arquivada: explica o motivo (US1.4)', async ({ page }) => {
    await simularApi(page, [{ name: 'Azul', isPrimary: true }]);
    await page.goto('/carteiras');
    await page.getByTestId('carteira').click();
    await page.getByRole('button', { name: 'Arquivar' }).click();
    await expect(page.getByTestId('erro-carteira')).toContainText('ela é a única ativa');
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  test('Tornar principal troca a marca', async ({ page }) => {
    await simularApi(page, [{ name: 'Azul', isPrimary: true }, { name: 'Verde' }]);
    await page.goto('/carteiras');
    await page.getByTestId('carteira').filter({ hasText: 'Verde' }).click();
    await page.getByRole('button', { name: 'Tornar principal' }).click();
    await expect(page.getByTestId('principal')).toBeVisible();
    await page.getByRole('link', { name: 'Voltar' }).click();
    await expect(
      page.getByTestId('carteira').filter({ hasText: 'Verde' }).getByTestId('principal'),
    ).toBeVisible();
    await expect(
      page.getByTestId('carteira').filter({ hasText: 'Azul' }).getByTestId('principal'),
    ).toHaveCount(0);
  });

  test('excluir sem lançamentos pede confirmação; com lançamentos só arquiva (US1.3)', async ({
    page,
  }) => {
    await simularApi(page, [
      { name: 'Azul', isPrimary: true, hasEntries: true },
      { name: 'Verde' },
    ]);
    await page.goto('/carteiras');

    await page.getByTestId('carteira').filter({ hasText: 'Azul' }).click();
    await expect(page.getByRole('button', { name: 'Excluir' })).toHaveCount(0);
    await expect(page.getByTestId('so-arquivar')).toBeVisible();
    await page.getByRole('link', { name: 'Voltar' }).click();

    await page.getByTestId('carteira').filter({ hasText: 'Verde' }).click();
    await page.getByRole('button', { name: 'Excluir' }).click();
    const dialogo = page.getByRole('dialog');
    await dialogo.getByRole('button', { name: 'Cancelar' }).click();
    await expect(dialogo).toHaveCount(0);
    await expect(page).toHaveURL(/\/carteiras\/w2$/);

    await page.getByRole('button', { name: 'Excluir' }).click();
    await page.getByRole('dialog').getByTestId('confirmar').click();
    await expect(page).toHaveURL(/\/carteiras$/);
    await expect(page.getByTestId('carteira')).toHaveCount(1);
  });

  test('"Carteiras" é alcançável pela navegação da casca (em "Mais" no celular)', async ({
    page,
  }) => {
    await simularApi(page);
    await page.goto('/');
    await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
    await irPara(page, 'Carteiras');
    await expect(page).toHaveURL(/\/carteiras$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Carteiras' })).toBeVisible();
  });
});

// Contra a stack real (E2E_BASE_URL); sem ela, são pulados.
test.describe('carteiras (US1) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa da stack real (E2E_BASE_URL)');

  async function criarConta(page: Page) {
    const email = `lia+${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
    await page.goto('/criar-conta');
    await page.getByLabel('Nome', { exact: true }).fill('Lia Souza');
    await page.getByLabel('E-mail').fill(email);
    await page.getByLabel('Senha', { exact: true }).fill('senha-teste-123');
    await page.getByRole('button', { name: 'Criar conta' }).click();
    await dispensarGuiaSeAberto(page);
  }

  async function nova(page: Page, nome: string, saldo: string) {
    await page.goto('/carteiras/nova');
    await page.getByLabel('Nome', { exact: true }).fill(nome);
    await page.getByLabel('Saldo inicial', { exact: true }).fill(saldo);
    await page.getByLabel('Data do saldo inicial').fill('2026-03-01');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page).toHaveURL(/\/carteiras$/);
  }

  test('US1.1 e US1.2: cadastra Azul (principal, R$ 500,00) e recusa "azul"', async ({ page }) => {
    await criarConta(page);
    await nova(page, 'Azul', '500,00');
    const linha = page.getByTestId('carteira').filter({ hasText: 'Azul' });
    await expect(linha).toContainText('R$ 500,00');
    await expect(linha.getByTestId('principal')).toBeVisible();

    await page.goto('/carteiras/nova');
    await page.getByLabel('Nome', { exact: true }).fill('azul');
    await page.getByRole('button', { name: 'Salvar' }).click();
    await expect(page.getByText('Já existe uma carteira com esse nome.')).toBeVisible();
  });

  test('US1.4: arquivar a principal exige outra; a única ativa é bloqueada', async ({ page }) => {
    await criarConta(page);
    await nova(page, 'Azul', '500,00');
    await page.getByTestId('carteira').click();
    await page.getByRole('button', { name: 'Arquivar' }).click();
    await expect(page.getByTestId('erro-carteira')).toContainText('única ativa');

    await nova(page, 'Verde', '10');
    await page.getByTestId('carteira').filter({ hasText: 'Azul' }).click();
    await page.getByRole('button', { name: 'Arquivar' }).click();
    const dialogo = page.getByRole('dialog');
    await dialogo.getByLabel('Nova carteira principal').selectOption({ label: 'Verde' });
    await dialogo.getByTestId('confirmar').click();
    await expect(page).toHaveURL(/\/carteiras$/);
    await expect(
      page.getByTestId('carteira').filter({ hasText: 'Verde' }).getByTestId('principal'),
    ).toBeVisible();

    await page.getByTestId('filtro-arquivadas').check();
    await expect(page.getByTestId('carteira').filter({ hasText: 'Azul' })).toBeVisible();
  });
});
