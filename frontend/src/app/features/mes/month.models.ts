import type { InvoiceSummary } from '../cartoes/card.models';
import type { CardPurchase } from '../lancamento/card-purchase.models';
import { Entry } from '../lancamento/entry.models';

/** Coluna do resumo (`SummaryColumn` do contrato): Previsto ou Realizado. */
export interface SummaryColumn {
  /** Saldo final(M−1) + saldos iniciais de carteiras com data em M (research R4). */
  openingBalanceCents: number;
  walletsInitialBalanceCents: number;
  incomeCents: number;
  expenseCents: number;
  resultCents: number;
  transfersInCents: number;
  transfersOutCents: number;
  finalBalanceCents: number;
}

/** `Transfer` do contrato (contracts/openapi.yaml). */
export interface Transfer {
  id: string;
  fromWalletId: string;
  toWalletId: string;
  amountCents: number;
  date: string;
  description: string;
  notes?: string | null;
  createdAt: string;
  version: number;
}

/**
 * `MonthInvoiceLine` do contrato (003 FR-021): fatura com vencimento no mês; Total, Pago e Restante
 * são da fatura inteira, `plannedCents`/`actualCents` já vêm no recorte do filtro de carteiras.
 */
export interface MonthInvoiceLine extends InvoiceSummary {
  creditCardName: string;
  plannedCents: number;
  actualCents: number;
}

/** Totais dos itens listados com filtro de tags (004 research R6). */
export interface FilterTotals {
  incomeCents: number;
  expenseCents: number;
  purchasesNetCents: number;
}

/** `MonthView` do contrato: resposta de `GET /months/{month}`. */
export interface MonthView {
  month: string;
  /** Hoje em America/Sao_Paulo, segundo o servidor. */
  today: string;
  walletIds: string[] | null;
  planned: SummaryColumn;
  actual: SummaryColumn;
  entries: Entry[];
  transfers: Transfer[];
  /** Faturas com vencimento no mês (003 FR-021). */
  invoices?: MonthInvoiceLine[];
  pending: { count: number; toReceiveCents: number; toPayCents: number };
  /** Só com filtro de tags: compras das faturas do mês com essas tags (research R6). */
  purchases?: CardPurchase[];
  /** Só com filtro de tags; o resumo continua o do mês inteiro. */
  filterTotals?: FilterTotals | null;
}
