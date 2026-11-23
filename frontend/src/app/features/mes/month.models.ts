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
  pending: { count: number; toReceiveCents: number; toPayCents: number };
}
