import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { criarEspacoReal, sessao } from './tags-api';

// 006 T007 — Exportar CSV no Perfil (US4.1, US4.2): lê o arquivo baixado de verdade.

const CABECALHO =
  'Tipo;Descrição;Data;Mês de referência;Valor;Situação;Carteira;Cartão;Fatura;Parcela;Tags';
const BOM = '﻿';

test.describe('exportar CSV (US4) — API simulada', () => {
  test('baixa o arquivo do intervalo escolhido e mostra erro do servidor', async ({ page }) => {
    await page.route('**/api/v1/auth/refresh', (route) => route.fulfill({ json: sessao }));
    const pedidos: string[] = [];
    await page.route('**/api/v1/exports/entries.csv**', (route) => {
      pedidos.push(new URL(route.request().url()).search);
      return route.fulfill({
        status: 200,
        headers: {
          'content-type': 'text/csv; charset=utf-8',
          'content-disposition': 'attachment; filename="fin-my-2026-03_2026-04.csv"',
        },
        body: `${BOM}${CABECALHO}\r\nEntrada;Salário Lia;05/03/2026;03/2026;3000,00;Recebida;Azul;;;;\r\n`,
      });
    });

    await page.goto('/perfil');
    await page.getByLabel('Mês inicial').fill('2026-03');
    await page.getByLabel('Mês final').fill('2026-04');
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Exportar', exact: true }).click();
    const arquivo = await download;

    expect(arquivo.suggestedFilename()).toBe('fin-my-2026-03_2026-04.csv');
    expect(pedidos).toEqual(['?from=2026-03&to=2026-04']);
    expect(await readFile(await arquivo.path(), 'utf8')).toContain('Salário Lia');
    await expect(page.getByTestId('exportado')).toContainText('fin-my-2026-03_2026-04.csv');

    await page.getByLabel('Mês final').fill('2026-02');
    await page.getByRole('button', { name: 'Exportar', exact: true }).click();
    await expect(page.getByTestId('erro-exportar')).toContainText('não pode ser anterior');
  });
});

test.describe('exportar CSV (US4) — stack real', () => {
  test.skip(!process.env['E2E_BASE_URL'], 'precisa do compose no ar e de E2E_BASE_URL');

  test('exporta março/2026 e o arquivo traz os lançamentos do período (US4.1, US4.2)', async ({
    page,
  }) => {
    const api = await criarEspacoReal(page);
    const bruna = await api.tag('Bruna', true);
    await api.entrada('income', 'Salário Lia', '3000,00', '2026-03-05');
    await api.entrada('expense', 'Almoço; "Léo"', '45,90', '2026-03-10', [bruna.id]);
    await api.entrada('income', 'Freela fora do período', '800,00', '2026-05-02');
    const cartao = await api.cartao('Verde', 18, 25);
    await api.compra(cartao.id, 'Padaria', '150,00', '2026-03-05');
    await api.compra(cartao.id, 'Mercado', '90,00', '2026-03-20');

    await page.goto('/perfil');
    await page.getByLabel('Mês inicial').fill('2026-03');
    await page.getByLabel('Mês final').fill('2026-03');
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Exportar', exact: true }).click();
    const arquivo = await download;

    expect(arquivo.suggestedFilename()).toBe('fin-my-2026-03_2026-03.csv');
    const texto = await readFile(await arquivo.path(), 'utf8');
    expect(texto.startsWith(BOM + CABECALHO)).toBe(true);
    const linhas = texto.slice(1).trim().split(/\r?\n/);
    expect(linhas).toContain('Entrada;Salário Lia;05/03/2026;03/2026;3000,00;Recebida;Azul;;;;');
    expect(linhas).toContain(
      'Saída;"Almoço; ""Léo""";10/03/2026;03/2026;-45,90;Paga;Azul;;;;Bruna',
    );
    // Compra pelo mês de vencimento da fatura: fecha dia 18, então 05/03 vence em março e 20/03 em abril
    expect(linhas).toContain('Compra;Padaria;05/03/2026;;-150,00;;Azul;Verde;03/2026;;');
    expect(texto).not.toContain('Mercado');
    expect(texto).not.toContain('Freela fora do período');
  });
});
