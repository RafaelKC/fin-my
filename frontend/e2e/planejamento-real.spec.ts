import { expect, test, type Page } from '@playwright/test';
import { criarEspacoReal } from './tags-api';

// 007 T007 — quickstart manual contra a stack real: Natal 1.200 e 13º salário 1.500 em dezembro
// (+300), estimativa de 800 no cartão Azul de janeiro a dezembro e "Copiar do ano anterior".
// Os números da grade conferem com a visão do mês devolvida pelo servidor (SC-002).

const celula = (page: Page, linha: string, mes: string) =>
  page.locator(`[data-linha="${linha}"] [data-testid="celula"][data-mes="${mes}"]`);
const total = (page: Page, qual: string, mes: string) =>
  page.locator(`[data-testid="total-${qual}"] [data-mes="${mes}"]`);

const MESES = Array.from({ length: 12 }, (_, i) => `2027-${String(i + 1).padStart(2, '0')}`);

test.describe('planejamento do ano (quickstart) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa do compose no ar e de E2E_BASE_URL');

  test('Natal, 13º salário, estimativa do cartão Azul e cópia para o ano seguinte', async ({
    page,
  }, info) => {
    test.skip(info.project.name === 'mobile', 'edição inline e cópia são do computador');
    const api = await criarEspacoReal(page);
    const azul = await api.cartao('Azul', 18, 25);

    await page.goto('/planejamento/2027');
    await expect(page.getByTestId('mes-coluna')).toHaveCount(12);

    const novaLinha = async (tipo: 'saida' | 'entrada', descricao: string) => {
      await page.getByTestId('nova-linha').click();
      await page.getByTestId(`nova-linha-${tipo}`).click();
      await page.getByTestId('nova-linha-descricao').fill(descricao);
      await page.getByTestId('nova-linha-criar').click();
    };
    const editar = async (linha: string, valor: string) => {
      await celula(page, linha, '2027-12').getByTestId('celula-valor').click();
      await page.getByTestId('celula-campo').fill(valor);
      await page.getByTestId('celula-campo').press('Enter');
    };

    await novaLinha('saida', 'Natal');
    await editar('expense:natal', '1200');
    await expect(celula(page, 'expense:natal', '2027-12')).toContainText('1.200,00');
    await novaLinha('entrada', '13º salário');
    await editar('income:13º salario', '1500');
    await expect(total(page, 'resultado', '2027-12')).toHaveText('R$ 300,00');

    // O servidor guardou de verdade: recarregar mostra o mesmo e a visão do mês concorda.
    await page.reload();
    await expect(celula(page, 'expense:natal', '2027-12')).toContainText('1.200,00');
    await expect(total(page, 'resultado', '2027-12')).toHaveText('R$ 300,00');

    // Estimativa de 800 no Azul de janeiro a dezembro (Preencher meses).
    const preencher = await page.request.post('/api/v1/years/2027/planning/fill', {
      headers: api.auth,
      data: { kind: 'invoice', creditCardId: azul.id, amount: '800', months: MESES },
    });
    expect(preencher.status(), await preencher.text()).toBe(200);
    expect((await preencher.json()).created).toBe(12);
    await page.reload();
    await expect(
      page.locator('[data-testid="bloco-faturas"]').getByTestId('celula-estimativa').first(),
    ).toContainText('800,00');
    await expect(total(page, 'resultado', '2027-12')).toContainText('500,00');
    await expect(total(page, 'resultado', '2027-12')).toContainText('-');

    for (const mes of ['2027-01', '2027-12']) {
      const visao = await (
        await page.request.get(`/api/v1/months/${mes}`, { headers: api.auth })
      ).json();
      const esperado = mes === '2027-12' ? -50000 : -80000;
      expect(visao.planned.resultCents).toBe(esperado);
      await expect(total(page, 'resultado', mes)).toContainText(
        (Math.abs(esperado) / 100).toLocaleString('pt-BR', { minimumFractionDigits: 2 }),
      );
    }

    // Copiar do ano anterior: 2028 recebe Natal, 13º e as estimativas.
    await page.goto('/planejamento/2028');
    await page.getByTestId('copiar-ano-anterior').click();
    await expect(page.getByTestId('copiar-linha')).toHaveCount(2);
    for (const caixa of await page.getByTestId('copiar-marcar').all()) await caixa.check();
    await page.getByTestId('copiar-estimativas').check();
    await page.getByTestId('copiar-confirmar').click();
    await expect(celula(page, 'expense:natal', '2028-12')).toContainText('1.200,00');
    await expect(celula(page, 'income:13º salario', '2028-12')).toContainText('1.500,00');
    await expect(total(page, 'resultado', '2028-12')).toContainText('500,00');
  });
});
