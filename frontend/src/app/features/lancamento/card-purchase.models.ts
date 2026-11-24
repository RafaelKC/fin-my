import type { CriadoVia } from '../../shared/selo-via-claude/selo-via-claude';
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
  /** Todas as tags, de acerto ou não (004 research R1). */
  tagIds?: string[];
}

export type PurchaseOrigin = 'manual' | 'installment' | 'carry_over' | 'recurrence';

/** `CardPurchase` do contrato. */
export interface CardPurchase {
  id: string;
  creditCardId: string;
  dueMonth: string;
  description: string;
  displayDescription: string;
  amountCents: number;
  nature: 'debit' | 'credit';
  date: string;
  origin: PurchaseOrigin;
  reconciled: boolean;
  manuallyMoved: boolean;
  notes?: string | null;
  installmentPlanId?: string | null;
  installmentNumber?: number | null;
  installmentTotal?: number | null;
  createdAt: string;
  /** Canal que criou o lançamento (008 FR-008); ausente = app. */
  createdVia?: CriadoVia;
  version: number;
  tagIds?: string[];
  settlementTagId?: string | null;
  splitGroupId?: string | null;
  splitCount?: number;
  /** Recorrência (005): vínculo, mês da ocorrência e se foi editada à mão (FR-005). */
  recurrenceId?: string | null;
  recurrenceMonth?: string | null;
  recurrenceModified?: boolean;
}

/** Escopo da edição/exclusão/movimento em parcelas (contracts/openapi.yaml). */
export type EscopoCompra = 'this' | 'this_and_following' | 'all';

/** Corpo de `PUT /card-purchases/{id}` (FR-015); `amount` é sempre positivo, o sinal vem de `nature`. */
export interface CardPurchaseUpdate {
  description: string;
  amount: string;
  nature: 'debit' | 'credit';
  date: string;
  creditCardId: string;
  notes: string | null;
  scope: 'this' | 'all';
  confirmPaidInvoice?: boolean;
  version: number;
  /** Aplicado conforme `scope` (research R1); ausente = não mexe nas tags. */
  tagIds?: string[];
}

export interface CardPurchaseResult {
  purchases: CardPurchase[];
  warnings: string[];
}

export const AVISO_CREDITO = 'recorded_as_credit';
