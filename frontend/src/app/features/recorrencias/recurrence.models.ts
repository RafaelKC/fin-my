export type RecurrenceKind = 'income' | 'expense' | 'card_purchase';
export type RecurrenceEndType = 'none' | 'until_month' | 'after_count';
export type ReferenceMonthRule = 'same_month' | 'next_month';
export type RecurrenceStatus = 'active' | 'ended';
export type RecurrenceReviewReason = 'wallet_archived' | 'card_archived' | 'tag_archived';

/** `Recurrence` do contrato (specs/005-recorrencias/contracts/api.md). */
export interface Recurrence {
  id: string;
  kind: RecurrenceKind;
  description: string;
  amountCents: number;
  dayOfMonth: number;
  walletId: string | null;
  creditCardId: string | null;
  tagIds: string[];
  startMonth: string;
  endType: RecurrenceEndType;
  endMonth: string | null;
  occurrenceLimit: number | null;
  referenceMonthRule: ReferenceMonthRule | null;
  generatedUntilMonth: string | null;
  status: RecurrenceStatus;
  nextOccurrenceDate: string | null;
  needsReview: boolean;
  reviewReason: RecurrenceReviewReason | null;
  version: number;
}

/** `RecurrenceInput` do contrato; `amount` é a expressão (RT-05) enviada como digitada. */
export interface RecurrenceInput {
  kind: RecurrenceKind;
  description: string;
  amount: string;
  dayOfMonth: number;
  walletId?: string | null;
  creditCardId?: string | null;
  tagIds: string[];
  startMonth: string;
  endType: RecurrenceEndType;
  endMonth?: string | null;
  occurrenceLimit?: number | null;
  /** Proibido em compra (o servidor recusa). */
  referenceMonthRule?: ReferenceMonthRule | null;
}

/** Corpo de `PUT /recurrences/{id}`: o modelo completo, `fromMonth` e a `version` lida. */
export interface RecurrenceUpdate extends RecurrenceInput {
  fromMonth: string;
  version: number;
}

/** Resposta de `GET /recurrences`: itens e o total mensal previsto das ativas (FR-009). */
export interface RecurrenceList {
  items: Recurrence[];
  monthlyIncomeCents: number;
  monthlyExpenseCents: number;
}

export interface RecurrenceUpdated extends Recurrence {
  affectedCount: number;
}

export interface RecurrenceEnded {
  recurrence: Recurrence;
  deletedCount: number;
  deletionBatchId: string;
}

export interface RecurrenceDeleted {
  deletedCount: number;
  unlinkedCount: number;
}

export const RECURRENCE_KINDS: readonly { value: RecurrenceKind; label: string }[] = [
  { value: 'expense', label: 'Saída' },
  { value: 'income', label: 'Entrada' },
  { value: 'card_purchase', label: 'Compra no cartão' },
];

export const RECURRENCE_END_TYPES: readonly { value: RecurrenceEndType; label: string }[] = [
  { value: 'none', label: 'Sem fim' },
  { value: 'until_month', label: 'Até um mês' },
  { value: 'after_count', label: 'Após N ocorrências' },
];

export const REFERENCE_MONTH_RULES: readonly { value: ReferenceMonthRule; label: string }[] = [
  { value: 'same_month', label: 'Mesmo mês da data' },
  { value: 'next_month', label: 'Mês seguinte à data' },
];

export const REVIEW_MESSAGES: Record<RecurrenceReviewReason, string> = {
  wallet_archived: 'Revise: carteira arquivada',
  card_archived: 'Revise: cartão arquivado',
  tag_archived: 'Revise: tag arquivada',
};

export const OCORRENCIAS_MAX = 360;
