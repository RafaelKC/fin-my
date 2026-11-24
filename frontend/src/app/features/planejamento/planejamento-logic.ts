import { normalizeText } from '../../core/money';
import { InvoiceRow, PlanningCell, PlanningKind, PlanningRow } from './planning.models';

/** Chave da linha como o servidor monta: `tipo:descrição normalizada` (FR-002). */
export function chaveDaLinha(kind: PlanningKind, description: string): string {
  return `${kind}:${normalizeText(description)}`;
}

/** Os 12 meses `AAAA-MM` do ano. */
export function mesesDoAno(ano: number): string[] {
  return Array.from({ length: 12 }, (_, i) => `${ano}-${String(i + 1).padStart(2, '0')}`);
}

/** Linha criada por "Nova linha": 12 células vazias e editáveis, que só existe na tela. */
export function linhaVazia(
  ano: number,
  kind: PlanningKind,
  description: string,
  walletId: string | null,
  day: number,
): PlanningRow {
  return {
    kind,
    key: chaveDaLinha(kind, description),
    description: description.trim(),
    latestWalletId: walletId,
    latestDay: day,
    latestTagIds: [],
    cells: mesesDoAno(ano).map((month) => ({
      month,
      plannedCents: 0,
      itemCount: 0,
      allSettled: false,
      editable: true,
    })),
  };
}

export function temItens(linha: PlanningRow): boolean {
  return linha.cells.some((c) => c.itemCount > 0);
}

/**
 * Troca (ou insere) a linha devolvida pelo servidor. Uma linha sem nenhum lançamento some do que o
 * servidor mostra (as de "Nova linha" continuam na tela por `mesclar`).
 */
export function aplicarLinha(linhas: readonly PlanningRow[], nova: PlanningRow): PlanningRow[] {
  const resto = linhas.filter((l) => l.key !== nova.key);
  return temItens(nova) ? [...resto, nova] : resto;
}

/** Linhas do servidor mais as de "Nova linha" ainda vazias, em ordem alfabética (FR-002). */
export function mesclar(
  servidor: readonly PlanningRow[],
  novas: readonly PlanningRow[],
): PlanningRow[] {
  const chaves = new Set(servidor.map((l) => l.key));
  return [...servidor, ...novas.filter((n) => !chaves.has(n.key))].sort((a, b) =>
    a.key.localeCompare(b.key, 'pt-BR'),
  );
}

export function aplicarFatura(linhas: readonly InvoiceRow[], nova: InvoiceRow): InvoiceRow[] {
  return linhas.map((l) => (l.creditCardId === nova.creditCardId ? nova : l));
}

export function ehLinhaDeFatura(linha: PlanningRow | InvoiceRow): linha is InvoiceRow {
  return 'creditCardId' in linha;
}

/** Valor mais frequente entre as células com valor; empate: o maior (research R3). `0` sem valores. */
export function valorMaisFrequente(cells: readonly PlanningCell[]): number {
  const contagem = new Map<number, number>();
  for (const c of cells) {
    if (c.itemCount > 0 && c.plannedCents > 0) {
      contagem.set(c.plannedCents, (contagem.get(c.plannedCents) ?? 0) + 1);
    }
  }
  let melhor = 0;
  let vezes = 0;
  for (const [valor, n] of contagem) {
    if (n > vezes || (n === vezes && valor > melhor)) {
      melhor = valor;
      vezes = n;
    }
  }
  return melhor;
}

/** Primeiro mês da linha com valor, para sugerir o início da recorrência. */
export function primeiroMesComValor(cells: readonly PlanningCell[]): string | null {
  return cells.find((c) => c.itemCount > 0 && c.plannedCents > 0)?.month ?? null;
}

/** Posição de uma célula na grade: a linha (id) e o mês. */
export interface CelulaRef {
  linha: string;
  mes: string;
}

/**
 * Próxima célula para Enter (`baixo`: mesma coluna, linha seguinte) e Tab (`direita`: mês seguinte,
 * passando para a próxima linha no fim). `null` ao sair do último/primeiro lugar.
 */
export function vizinha(
  linhas: readonly string[],
  meses: readonly string[],
  atual: CelulaRef,
  direcao: 'baixo' | 'cima' | 'direita' | 'esquerda',
): CelulaRef | null {
  const r = linhas.indexOf(atual.linha);
  const m = meses.indexOf(atual.mes);
  if (r < 0 || m < 0) return null;
  switch (direcao) {
    case 'baixo':
      return r + 1 < linhas.length ? { linha: linhas[r + 1], mes: atual.mes } : null;
    case 'cima':
      return r > 0 ? { linha: linhas[r - 1], mes: atual.mes } : null;
    case 'direita':
      if (m + 1 < meses.length) return { linha: atual.linha, mes: meses[m + 1] };
      return r + 1 < linhas.length ? { linha: linhas[r + 1], mes: meses[0] } : null;
    case 'esquerda':
      if (m > 0) return { linha: atual.linha, mes: meses[m - 1] };
      return r > 0 ? { linha: linhas[r - 1], mes: meses[meses.length - 1] } : null;
  }
}
