import { EntradaPreenchida } from '../lancamento/lancamento.sheet';
import { SettlementItem, SettlementRow } from './settlement.models';

/** Número do cartão de acerto que abre a lista de lançamentos que o compõem (FR-009). */
export type NumeroDetalhavel = 'spent' | 'charged' | 'received';

/** `AAAA-MM` → `MM/AAAA` (descrição da cobrança, US3.1). */
function mesBarra(month: string): string {
  return `${month.slice(5, 7)}/${month.slice(0, 4)}`;
}

/**
 * Entrada de "Gerar cobrança" (FR-010, research R4): descrição "<Tag> — acerto MM/AAAA", valor =
 * A cobrar, Mês de referência M, data = hoje se M é o mês de hoje, senão o dia 1 de M.
 */
export function montarCobranca(row: SettlementRow, month: string, hoje: string): EntradaPreenchida {
  return {
    description: `${row.tagName} — acerto ${mesBarra(month)}`,
    amountCents: row.toChargeCents,
    date: month === hoje.slice(0, 7) ? hoje : `${month}-01`,
    referenceMonth: month,
    tagIds: [row.tagId],
  };
}

/** "Gerar cobrança" só aparece com A cobrar > 0 (US3.3). */
export function podeGerarCobranca(row: SettlementRow): boolean {
  return row.toChargeCents > 0;
}

/** Itens de um número: Gasto = `spent`; Cobrado = `charged`; Recebido = `charged` já recebidas. */
export function itensDoNumero(
  itens: readonly SettlementItem[],
  numero: NumeroDetalhavel,
): SettlementItem[] {
  if (numero === 'spent') return itens.filter((i) => i.group === 'spent');
  const cobrados = itens.filter((i) => i.group === 'charged');
  return numero === 'charged' ? cobrados : cobrados.filter((i) => i.received === true);
}

/** US2.3: saldo acumulado negativo é crédito da pessoa. */
export function ehCredito(row: SettlementRow): boolean {
  return row.accumulatedCents < 0;
}
