import type { InvoiceStatus } from '../cartoes/card.models';
import type { SummaryColumn } from '../mes/month.models';

/** Totais do ano numa leitura (`totals.planned` / `totals.actual`). */
export interface YearTotals {
  incomeCents: number;
  expenseCents: number;
  resultCents: number;
  finalBalanceCents: number;
}

/** `GET /years/{yyyy}/panel` (006 contracts/api.md). */
export interface YearPanel {
  year: number;
  today: string;
  months: { month: string; planned: SummaryColumn; actual: SummaryColumn }[];
  totals: { planned: YearTotals; actual: YearTotals };
}

/** `GET /now` (006 contracts/api.md). */
export interface NowView {
  today: string;
  wallets: { id: string; name: string; color: string; currentBalanceCents: number }[];
  archivedWalletsBalanceCents: number;
  totalBalanceCents: number;
  invoices: {
    creditCardId: string;
    creditCardName: string;
    dueMonth: string;
    dueDate: string;
    status: InvoiceStatus;
    remainingCents: number;
  }[];
  pending: { count: number; toReceiveCents: number; toPayCents: number };
  settlements: { tagId: string; tagName: string; accumulatedCents: number }[];
}
