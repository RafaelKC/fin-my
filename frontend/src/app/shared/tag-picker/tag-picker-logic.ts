import { normalizeText } from '../../core/money';
import { CHIPS_FREQUENTES, Tag } from './tag.models';

/** Ordem das chips (research R2): mais usadas nos últimos 90 dias primeiro, depois o nome. */
export function ordenarPorUso(tags: readonly Tag[]): Tag[] {
  return [...tags].sort(
    (a, b) =>
      b.uses90d - a.uses90d || a.name.localeCompare(b.name, 'pt-BR', { sensitivity: 'base' }),
  );
}

/** As 8 tags ativas mais usadas: os botões de um toque (US1.1). */
export function chipsFrequentes(tags: readonly Tag[]): Tag[] {
  return ordenarPorUso(tags.filter((t) => !t.archived)).slice(0, CHIPS_FREQUENTES);
}

/** Tag ativa cujo nome, normalizado, é igual ao texto (US1.3: "bruna" usa "Bruna"). */
export function tagComNome(tags: readonly Tag[], texto: string): Tag | null {
  const alvo = normalizeText(texto);
  if (alvo === '') return null;
  return tags.find((t) => normalizeText(t.name) === alvo) ?? null;
}

/** Tags ativas, fora as já escolhidas, cujo nome contém o texto digitado (sem acentos/caixa). */
export function buscarTags(
  tags: readonly Tag[],
  texto: string,
  escolhidas: readonly string[],
): Tag[] {
  const alvo = normalizeText(texto);
  if (alvo === '') return [];
  return ordenarPorUso(
    tags.filter(
      (t) => !t.archived && !escolhidas.includes(t.id) && normalizeText(t.name).includes(alvo),
    ),
  );
}
