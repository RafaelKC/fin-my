/** Previsto da fatura com Estimativa (007 FR-010, research R1): espelha `InvoiceForecast` do backend. */

/** A estimativa só vale enquanto a fatura não fechou: hoje < fechamento (datas `AAAA-MM-DD`). */
export function estimateActive(
  estimateCents: number | null,
  today: string,
  closingDate: string,
): boolean {
  return estimateCents !== null && today < closingDate;
}

/**
 * Com estimativa válida: `max(total, pago, estimativa, 0)`; sem ela, ou depois do fechamento:
 * `max(total, pago, 0)` (003 FR-022).
 */
export function plannedInvoice(
  totalCents: number,
  paidCents: number,
  estimateCents: number | null,
  today: string,
  closingDate: string,
): number {
  const withoutEstimate = Math.max(totalCents, paidCents, 0);
  return estimateActive(estimateCents, today, closingDate) && estimateCents !== null
    ? Math.max(withoutEstimate, estimateCents)
    : withoutEstimate;
}
