import type { CardPurchase } from '../../lancamento/card-purchase.models';
import { fatura } from '../testing';
import type { Invoice, InvoicePayment } from './invoice.models';

export function compra(parcial: Partial<CardPurchase> = {}): CardPurchase {
  return {
    id: 'p1',
    creditCardId: 'c-azul',
    dueMonth: '2026-03',
    description: 'Uber',
    displayDescription: 'Uber',
    amountCents: 1295,
    nature: 'debit',
    date: '2026-03-10',
    origin: 'manual',
    reconciled: false,
    manuallyMoved: false,
    notes: null,
    installmentPlanId: null,
    installmentNumber: null,
    installmentTotal: null,
    createdAt: '2026-03-10T12:00:00Z',
    version: 1,
    ...parcial,
  };
}

export function pagamento(parcial: Partial<InvoicePayment> = {}): InvoicePayment {
  return {
    id: 'pg1',
    creditCardId: 'c-azul',
    dueMonth: '2026-03',
    walletId: 'w-azul',
    amountCents: 15000,
    date: '2026-03-20',
    version: 1,
    ...parcial,
  };
}

/** Fatura de março do exemplo da US5 (Total R$ 200,00, Fechada). */
export function faturaDetalhada(parcial: Partial<Invoice> = {}): Invoice {
  return {
    ...fatura({ status: 'closed', totalCents: 20000, remainingCents: 20000 }),
    reconciledCents: 0,
    canCarryOver: true,
    purchases: [],
    payments: [],
    previousMonth: '2026-02',
    nextMonth: '2026-04',
    ...parcial,
  };
}
