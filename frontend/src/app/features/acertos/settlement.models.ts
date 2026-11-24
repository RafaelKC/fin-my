/** Linha de `GET /settlements/{month}` (004 contracts/openapi.yaml); dinheiro em centavos. */
export interface SettlementRow {
  tagId: string;
  tagName: string;
  tagColor: string;
  archived: boolean;
  spentCents: number;
  chargedCents: number;
  receivedCents: number;
  toChargeCents: number;
  toReceiveCents: number;
  /** Negativo = crédito da pessoa (ela pagou a mais). */
  accumulatedCents: number;
}

export type SettlementGroup = 'spent' | 'charged';

/** Lançamento que compõe um número dos acertos (`.../tags/{tagId}/items`). */
export interface SettlementItem {
  group: SettlementGroup;
  type: 'income' | 'expense' | 'card_purchase';
  id: string;
  description: string;
  date: string;
  /** Crédito de compra vem negativo. */
  signedCents: number;
  /** Só nas entradas: já foi recebida? */
  received?: boolean | null;
  creditCardId?: string | null;
}
