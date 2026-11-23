import { expect, type Page } from '@playwright/test';

/** Vai para um item da navegação principal; no celular, "Carteiras" e "Perfil" ficam em "Mais". */
export async function irPara(page: Page, rotulo: string): Promise<void> {
  const nav = page.getByRole('navigation', { name: 'Principal' });
  const mais = nav.getByTestId('mais');
  if ((rotulo === 'Carteiras' || rotulo === 'Perfil') && (await mais.isVisible())) {
    await mais.click();
    await expect(mais).toHaveAttribute('aria-expanded', 'true');
  }
  await nav.getByRole('link', { name: rotulo }).click();
}
