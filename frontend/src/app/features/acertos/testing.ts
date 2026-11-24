import { SettlementRow } from './settlement.models';

/** Bruna em março/2026, exemplo da US2 (SC-002). */
export function linha(parcial: Partial<SettlementRow> = {}): SettlementRow {
  return {
    tagId: 't-bruna',
    tagName: 'Bruna',
    tagColor: '#3E63DD',
    archived: false,
    spentCents: 20000,
    chargedCents: 18000,
    receivedCents: 3000,
    toChargeCents: 2000,
    toReceiveCents: 17000,
    accumulatedCents: 17000,
    ...parcial,
  };
}
