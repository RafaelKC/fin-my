import { normalizeText } from '../../core/money';
import {
  InvoiceRow,
  PlanningCell,
  PlanningGrid,
  PlanningKind,
  PlanningRow,
  PlanningTotals,
} from './planning.models';

const MESES = Array.from({ length: 12 }, (_, i) => `2027-${String(i + 1).padStart(2, '0')}`);

/** Célula com um lançamento Previsto (ou vazia se `cents` for 0). */
export function celula(
  mes: number,
  cents: number,
  parcial: Partial<PlanningCell> = {},
): PlanningCell {
  return {
    month: MESES[mes - 1],
    plannedCents: cents,
    itemCount: cents > 0 ? 1 : 0,
    allSettled: false,
    editable: true,
    entryId: cents > 0 ? `e-${mes}-${cents}` : null,
    ...parcial,
  };
}

/** Linha de 12 meses; `valores` é `{ mês: centavos }`. */
export function linha(
  kind: PlanningKind,
  description: string,
  valores: Record<number, number | (Partial<PlanningCell> & { cents: number })> = {},
): PlanningRow {
  return {
    kind,
    key: `${kind}:${normalizeText(description)}`,
    description,
    latestWalletId: 'w-azul',
    latestDay: 10,
    latestTagIds: [],
    cells: MESES.map((_, i) => {
      const v = valores[i + 1];
      if (v === undefined) return celula(i + 1, 0);
      return typeof v === 'number' ? celula(i + 1, v) : celula(i + 1, v.cents, v);
    }),
  };
}

export function faturaAzul(estimativas: Record<number, number> = {}): InvoiceRow {
  return {
    creditCardId: 'c-azul',
    creditCardName: 'Azul',
    archived: false,
    cells: MESES.map((month, i) => ({
      month,
      plannedCents: estimativas[i + 1] ?? 20553,
      totalCents: 20553,
      estimateCents: estimativas[i + 1] ?? null,
      editable: true,
    })),
  };
}

export function totais(resultadoDez = 0): PlanningTotals {
  return {
    months: MESES.map((month, i) => {
      const dez = i === 11;
      const income = dez ? 150000 : 500000;
      const expense = dez ? 150000 - resultadoDez : 200000;
      return {
        month,
        incomeCents: income,
        expenseCents: expense,
        resultCents: income - expense,
        finalBalanceCents: (i + 1) * 100000,
      };
    }),
  };
}

/** Grade de 2027: Salário (entrada), Tim (saída, março com 2 lançamentos), Natal só em novembro, fatura Azul. */
export function grade2027(): PlanningGrid {
  return {
    year: 2027,
    today: '2027-03-20',
    incomeRows: [linha('income', 'Salário', { 1: 500000, 2: 500000, 3: 500000 })],
    expenseRows: [
      linha('expense', 'Natal', { 11: 80000 }),
      linha('expense', 'Tim', {
        1: 4499,
        2: 4499,
        3: { cents: 9998, itemCount: 2, editable: false, entryId: null },
        4: { cents: 4499, allSettled: true, editable: false },
      }),
    ],
    invoiceRows: [faturaAzul()],
    totals: totais(),
  };
}
