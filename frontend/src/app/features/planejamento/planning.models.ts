/** Tipos da grade do ano (007 contracts/api.md). */

export type PlanningKind = 'income' | 'expense';
export type PlanningCellKind = PlanningKind | 'invoice';

export interface PlanningCell {
  month: string;
  plannedCents: number;
  itemCount: number;
  allSettled: boolean;
  editable: boolean;
  entryId?: string | null;
}

export interface PlanningRow {
  kind: PlanningKind;
  key: string;
  description: string;
  latestWalletId?: string | null;
  latestDay?: number | null;
  latestTagIds: string[];
  cells: PlanningCell[];
}

export interface InvoiceCell {
  month: string;
  plannedCents: number;
  totalCents: number;
  estimateCents?: number | null;
  editable: boolean;
}

export interface InvoiceRow {
  creditCardId: string;
  creditCardName: string;
  archived: boolean;
  cells: InvoiceCell[];
}

export interface PlanningTotalsMonth {
  month: string;
  incomeCents: number;
  expenseCents: number;
  resultCents: number;
  finalBalanceCents: number;
}

export interface PlanningTotals {
  months: PlanningTotalsMonth[];
}

/** `GET /years/{yyyy}/planning`. */
export interface PlanningGrid {
  year: number;
  today: string;
  incomeRows: PlanningRow[];
  expenseRows: PlanningRow[];
  invoiceRows: InvoiceRow[];
  totals: PlanningTotals;
}

/** Corpo de `PUT /years/{yyyy}/planning/cells` (research R3). */
export interface PlanningCellInput {
  kind: PlanningCellKind;
  description?: string;
  creditCardId?: string;
  month: string;
  value: string;
  walletId?: string | null;
  day?: number | null;
}

export interface PlanningCellResult {
  row: PlanningRow | InvoiceRow;
  totals: PlanningTotals;
  deletionBatchId?: string | null;
}

/** Corpo de `POST /years/{yyyy}/planning/fill` (research R3). */
export interface PlanningFillInput {
  kind: PlanningCellKind;
  description?: string;
  creditCardId?: string;
  amount: string;
  months: string[];
  walletId?: string | null;
  day?: number | null;
}

export interface PlanningFillResult {
  created: number;
  skipped: number;
  row: PlanningRow | InvoiceRow;
  totals: PlanningTotals;
}

/** Lançamento de uma célula não editável (extensão `items` do 409 `planning.cell_not_editable`). */
export interface PlanningCellItem {
  id: string;
  description: string;
  amountCents: number;
  date: string;
  status: string;
  walletId: string;
}

export const CELULA_NAO_EDITAVEL = 'planning.cell_not_editable';

/** Linha candidata à cópia (`GET .../planning/copy-candidates`, US3.1). */
export interface CopyCandidateRow {
  key: string;
  kind: PlanningKind;
  description: string;
  settlementTagName?: string | null;
  cells: { month: string; plannedCents: number; itemCount: number }[];
}

export interface CopyEstimateCard {
  creditCardId: string;
  creditCardName: string;
  months: { month: string; estimateCents: number }[];
}

export interface CopyCandidates {
  rows: CopyCandidateRow[];
  estimates: CopyEstimateCard[];
}

/** Corpo de `POST .../planning/copy`. */
export interface PlanningCopyInput {
  rowKeys: string[];
  copyEstimates: boolean;
}

export interface PlanningCopyResult {
  created: number;
  skipped: number;
}
