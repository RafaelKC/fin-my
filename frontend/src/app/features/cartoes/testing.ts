import { CreditCardOverview, InvoiceSummary } from './card.models';

export function fatura(parcial: Partial<InvoiceSummary> = {}): InvoiceSummary {
  return {
    creditCardId: 'c-azul',
    dueMonth: '2026-03',
    closingDate: '2026-03-18',
    dueDate: '2026-03-25',
    id: null,
    status: 'no_amount_due',
    totalCents: 0,
    paidCents: 0,
    remainingCents: 0,
    ...parcial,
  };
}

export function cartao(parcial: Partial<CreditCardOverview> = {}): CreditCardOverview {
  return {
    id: 'c-azul',
    name: 'Azul',
    closingDay: 18,
    dueDay: 25,
    paymentWalletId: 'w-azul',
    color: '#1E88E5',
    limitCents: null,
    archived: false,
    hasPurchases: false,
    version: 1,
    currentInvoice: fatura(),
    nextInvoice: fatura({
      dueMonth: '2026-04',
      closingDate: '2026-04-18',
      dueDate: '2026-04-25',
    }),
    openPastInvoices: [],
    availableLimitCents: null,
    ...parcial,
  };
}
