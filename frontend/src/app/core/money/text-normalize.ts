/**
 * Normalização de texto para comparação e busca: sem espaços nas pontas, sem acentos e em minúsculas.
 * Espaços internos são preservados. Ex.: "  Ação Ç " → "acao c".
 */
export function normalizeText(text: string): string {
  return text
    .trim()
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .normalize('NFC')
    .toLowerCase();
}
