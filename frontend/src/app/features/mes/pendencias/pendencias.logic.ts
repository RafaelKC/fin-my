import { Entry } from '../../lancamento/entry.models';

/** "1 pendência de meses anteriores" / "3 pendências de meses anteriores" (FR-019). */
export function tituloAviso(count: number): string {
  return `${count} ${count === 1 ? 'pendência' : 'pendências'} de meses anteriores`;
}

/** Rótulo da ação de realizar conforme o que está selecionado (Recebida/Paga). */
export function rotuloRealizar(selecionadas: readonly Entry[]): string {
  const entradas = selecionadas.filter((e) => e.kind === 'income').length;
  if (selecionadas.length > 0 && entradas === selecionadas.length) return 'Marcar como recebida';
  if (entradas === 0 && selecionadas.length > 0) return 'Marcar como paga';
  return 'Marcar como recebida/paga';
}

export function mensagemExclusao(afetadas: number): string {
  return afetadas === 1 ? 'Lançamento excluído.' : `${afetadas} lançamentos excluídos.`;
}
