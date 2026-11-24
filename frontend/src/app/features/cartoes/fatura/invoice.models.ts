import type { CardPurchase } from '../../lancamento/card-purchase.models';
import type { InvoiceSummary } from '../card.models';

/** `InvoicePayment` do contrato. */
export interface InvoicePayment {
  id: string;
  creditCardId: string;
  dueMonth: string;
  walletId: string;
  amountCents: number;
  date: string;
  version: number;
}

/** Subtotal por Tag de acerto da fatura (FR-011); `tagId` nulo = "Sem tag de acerto". */
export interface SettlementSubtotal {
  tagId: string | null;
  cents: number;
}

/** `Invoice` do contrato: resumo + conferido, compras, pagamentos e meses vizinhos. */
export interface Invoice extends InvoiceSummary {
  reconciledCents: number;
  canCarryOver: boolean;
  purchases: CardPurchase[];
  payments: InvoicePayment[];
  previousMonth: string;
  nextMonth: string;
  /** Só com filtro de tags: Débitos − Créditos das compras listadas (004 research R6). */
  filteredTotalCents?: number | null;
  /** Bloco "Por pessoa": sempre soma o Total da fatura, com ou sem filtro (SC-004). */
  settlementSubtotals?: SettlementSubtotal[];
}

/** Corpo de `POST .../payments`; vazio = padrões (FR-017). */
export interface InvoicePaymentCreate {
  amount?: string;
  date?: string;
  walletId?: string;
}

/** Corpo de `PUT /invoice-payments/{id}`. */
export interface InvoicePaymentUpdate {
  amount: string;
  date: string;
  walletId: string;
  version: number;
}
