import { expect, type Page } from '@playwright/test';

/** Vai para um item da navegação principal; no celular, "Painel", "Carteiras", "Recorrências" e "Perfil" ficam em "Mais". */
export async function irPara(page: Page, rotulo: string): Promise<void> {
  const nav = page.getByRole('navigation', { name: 'Principal' });
  const mais = nav.getByTestId('mais');
  if (
    (rotulo === 'Painel' ||
      rotulo === 'Carteiras' ||
      rotulo === 'Recorrências' ||
      rotulo === 'Busca' ||
      rotulo === 'Tags' ||
      rotulo === 'Perfil') &&
    (await mais.isVisible())
  ) {
    await mais.click();
    await expect(mais).toHaveAttribute('aria-expanded', 'true');
  }
  await nav.getByRole('link', { name: rotulo }).click();
}

/**
 * O primeiro uso do formulário rápido abre em "Cartão" (003 FR-010). Os testes de Saída/Entrada/
 * Transferência partem do último tipo "Saída": grava a preferência do aparelho se ainda não houver.
 */
export async function comecarEmSaida(page: Page): Promise<void> {
  await page.addInitScript(() => {
    try {
      if (localStorage.getItem('finmy.prefs.v1') === null) {
        localStorage.setItem('finmy.prefs.v1', JSON.stringify({ lastKind: 'expense' }));
      }
    } catch {
      // armazenamento indisponível: segue com o padrão
    }
  });
}
