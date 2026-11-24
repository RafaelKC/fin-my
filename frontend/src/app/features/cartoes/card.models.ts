export type InvoiceStatus = 'no_amount_due' | 'paid' | 'open' | 'closed' | 'overdue';

/** `CreditCard` do contrato (contracts/openapi.yaml). */
export interface CreditCard {
  id: string;
  name: string;
  closingDay: number;
  dueDay: number;
  paymentWalletId: string;
  color: string;
  limitCents?: number | null;
  archived: boolean;
  hasPurchases: boolean;
  version: number;
}

/** `CreditCardInput` do contrato; `limit` é uma expressão (RT-05) enviada como digitada, vazio = sem limite. */
export interface CreditCardInput {
  name: string;
  closingDay: number;
  dueDay: number;
  paymentWalletId: string;
  color: string;
  limit: string | null;
}

/** `InvoiceSummary` do contrato. */
export interface InvoiceSummary {
  creditCardId: string;
  /** Mês de vencimento, `AAAA-MM`. */
  dueMonth: string;
  closingDate: string;
  dueDate: string;
  id?: string | null;
  status: InvoiceStatus;
  totalCents: number;
  paidCents: number;
  remainingCents: number;
  /** Estimativa de gasto da fatura (007 FR-009); fora de Total, Restante e limite. */
  estimateCents?: number | null;
  /** A estimativa ainda vale (hoje < fechamento): o Previsto usa `max(total, pago, estimativa)`. */
  estimateActive?: boolean;
}

/** `CreditCardOverview` do contrato: cartão com fatura atual, próxima, em aberto e limite. */
export interface CreditCardOverview extends CreditCard {
  currentInvoice: InvoiceSummary;
  nextInvoice: InvoiceSummary;
  openPastInvoices: InvoiceSummary[];
  availableLimitCents?: number | null;
}

/** Paleta fixa de 10 cores (mesma das carteiras, FR-001). */
export { WALLET_COLORS as CARD_COLORS } from '../carteiras/wallet.models';

export const NOME_CARTAO_MAX = 40;
