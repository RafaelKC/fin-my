import { Recurrence } from './recurrence.models';

/** Recorrência fictícia "Tim" (US1.1): Saída de R$ 44,99, dia 10, carteira Azul. */
export function recorrencia(parcial: Partial<Recurrence> = {}): Recurrence {
  return {
    id: 'r-tim',
    kind: 'expense',
    description: 'Tim',
    amountCents: 4499,
    dayOfMonth: 10,
    walletId: 'w-azul',
    creditCardId: null,
    tagIds: [],
    startMonth: '2026-03',
    endType: 'none',
    endMonth: null,
    occurrenceLimit: null,
    referenceMonthRule: 'same_month',
    generatedUntilMonth: '2027-12',
    status: 'active',
    nextOccurrenceDate: '2026-03-10',
    needsReview: false,
    reviewReason: null,
    version: 1,
    ...parcial,
  };
}
