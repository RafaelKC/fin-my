import { expect, type Page } from '@playwright/test';

/**
 * Depois de criar a conta pela tela, um Espaço sem carteiras abre o guia de primeiros passos
 * (006 FR-007). Os testes da stack real que só querem o app seguem dispensando o guia.
 */
export async function dispensarGuiaSeAberto(page: Page): Promise<void> {
  await expect(page).toHaveURL(/\/(mes\/\d{4}-\d{2}|primeiros-passos\/[a-z-]+)$/);
  if (page.url().includes('/primeiros-passos/')) {
    await page.getByTestId('dispensar').click();
  }
  await expect(page).toHaveURL(/\/mes\/\d{4}-\d{2}$/);
}
