import type { InstallmentMode } from '../../core/money';

/** Códigos de erro da API de compras (contracts/openapi.yaml). */
export const CODIGO_FATURA_PAGA = 'card_purchase.invoice_paid_confirmation_required';

/** `CardPurchaseCreate.installments` do contrato; ausente = à vista. */
export interface CardPurchaseInstallments {
  total: number;
  mode: InstallmentMode;
  /** Só `in_progress` (p). */
  nextNumber?: number;
  /** Só `in_progress`: fatura (AAAA-MM) da parcela p. */
  firstInvoiceMonth?: string;
}

/** `CardPurchaseCreate` do contrato; `amount` é a expressão (RT-05), negativa = estorno (R6). */
export interface CardPurchaseCreate {
  description: string;
  amount: string;
  date: string;
  creditCardId: string;
  notes?: string | null;
  installments?: CardPurchaseInstallments;
  confirmPaidInvoice?: boolean;
}

/** `CardPurchase` do contrato (só o que o formulário usa). */
export interface CardPurchase {
  id: string;
  creditCardId: string;
  dueMonth: string;
  description: string;
  displayDescription: string;
  amountCents: number;
  nature: 'debit' | 'credit';
  date: string;
  installmentNumber?: number | null;
  installmentTotal?: number | null;
  version: number;
}

export interface CardPurchaseResult {
  purchases: CardPurchase[];
  warnings: string[];
}

export const AVISO_CREDITO = 'recorded_as_credit';
