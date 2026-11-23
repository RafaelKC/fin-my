import { normalizeText } from '../../core/money';
import { Entry, EntryStatus } from '../lancamento/entry.models';

/** Filtro de situação da visão do mês (query string `situacao`); `null` = todas (FR-018). */
export type FiltroSituacao = EntryStatus | null;

export type ColunaOrdem = 'description' | 'date' | 'amount' | 'wallet' | 'status';
export interface Ordem {
  coluna: ColunaOrdem;
  direcao: 'asc' | 'desc';
}

/** Menor e maior mês aceitos pela API (RT-03). */
export const MES_MINIMO = '2000-01';
export const MES_MAXIMO = '2100-12';

/** `?carteiras=a,b` → `['a', 'b']`; ausente → todas (lista vazia). */
export function lerCarteiras(valor: string | null): string[] {
  if (valor === null) return [];
  return [
    ...new Set(
      valor
        .split(',')
        .map((id) => id.trim())
        .filter((id) => id !== ''),
    ),
  ];
}

/** `?situacao=` só aceita `planned` e `settled`; qualquer outra coisa = todas. */
export function lerSituacao(valor: string | null): FiltroSituacao {
  return valor === 'planned' || valor === 'settled' ? valor : null;
}

/** Query string a manter ao navegar entre meses; omite o que está no padrão. */
export function montarQuery(
  carteiras: readonly string[],
  situacao: FiltroSituacao,
): Record<string, string | null> {
  return {
    carteiras: carteiras.length > 0 ? carteiras.join(',') : null,
    situacao,
  };
}

export function filtrarPorSituacao(entries: readonly Entry[], situacao: FiltroSituacao): Entry[] {
  return situacao === null ? [...entries] : entries.filter((e) => e.status === situacao);
}

/** Prevista com data anterior a hoje ganha o selo "Atrasada" (Edge Cases da spec). */
export function estaAtrasada(entry: Entry, hoje: string): boolean {
  return entry.status === 'planned' && entry.date < hoje;
}

/** Ordena para a tabela do computador (FR-026); sem `ordem`, mantém a ordem do servidor (RT-12). */
export function ordenar(
  entries: readonly Entry[],
  ordem: Ordem | null,
  nomeCarteira: (walletId: string) => string,
): Entry[] {
  if (ordem === null) return [...entries];
  const sinal = ordem.direcao === 'asc' ? 1 : -1;
  const chave = (e: Entry): string | number => {
    switch (ordem.coluna) {
      case 'description':
        return normalizeText(e.description);
      case 'date':
        return e.date;
      case 'amount':
        return e.amountCents;
      case 'wallet':
        return normalizeText(nomeCarteira(e.walletId));
      case 'status':
        return e.status === 'planned' ? 0 : 1;
    }
  };
  // `sort` é estável: empates mantêm a ordem do servidor.
  return [...entries].sort((a, b) => {
    const x = chave(a);
    const y = chave(b);
    return x < y ? -sinal : x > y ? sinal : 0;
  });
}

/** Próxima ordem ao clicar no cabeçalho: nova coluna → crescente; mesma coluna → inverte. */
export function alternarOrdem(atual: Ordem | null, coluna: ColunaOrdem): Ordem {
  return atual?.coluna === coluna
    ? { coluna, direcao: atual.direcao === 'asc' ? 'desc' : 'asc' }
    : { coluna, direcao: 'asc' };
}

/** Deslizar horizontal: devolve -1 (mês anterior), 1 (seguinte) ou 0 (não foi um deslize). */
export function direcaoDoDeslize(dx: number, dy: number): -1 | 0 | 1 {
  const LIMITE = 60;
  if (Math.abs(dx) < LIMITE || Math.abs(dx) < Math.abs(dy) * 1.5) return 0;
  return dx > 0 ? -1 : 1;
}

/** `2026-03-25` → `25/03/2026` (RT-10). */
export function formatarData(data: string): string {
  const [ano, mes, dia] = data.split('-');
  return `${dia}/${mes}/${ano}`;
}
