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

/** `Invoice` do contrato: resumo + conferido, compras, pagamentos e meses vizinhos. */
export interface Invoice extends InvoiceSummary {
  reconciledCents: number;
  canCarryOver: boolean;
  purchases: CardPurchase[];
  payments: InvoicePayment[];
  previousMonth: string;
  nextMonth: string;
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
