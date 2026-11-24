import { SummaryColumn } from '../mes/month.models';
import { NowView, YearPanel } from './panel.models';

export function coluna(income: number, expense: number, final: number): SummaryColumn {
  return {
    openingBalanceCents: 0,
    walletsInitialBalanceCents: 0,
    incomeCents: income,
    expenseCents: expense,
    resultCents: income - expense,
    transfersInCents: 0,
    transfersOutCents: 0,
    finalBalanceCents: final,
  };
}

/** Painel de 2026: JAN com resultado positivo, FEV negativo (US1.2), demais meses zerados. */
export function painel2026(): YearPanel {
  const meses = Array.from({ length: 12 }, (_, i) => {
    const month = `2026-${String(i + 1).padStart(2, '0')}`;
    if (i === 0)
      return {
        month,
        planned: coluna(500000, 200000, 300000),
        actual: coluna(450000, 200000, 250000),
      };
    if (i === 1)
      return { month, planned: coluna(100000, 250000, 150000), actual: coluna(0, 0, 250000) };
    return { month, planned: coluna(0, 0, 150000), actual: coluna(0, 0, 250000) };
  });
  return {
    year: 2026,
    today: '2026-03-20',
    months: meses,
    totals: {
      planned: {
        incomeCents: 600000,
        expenseCents: 450000,
        resultCents: 150000,
        finalBalanceCents: 150000,
      },
      actual: {
        incomeCents: 450000,
        expenseCents: 200000,
        resultCents: 250000,
        finalBalanceCents: 250000,
      },
    },
  };
}

/** US2: carteiras Azul e Verde (R$ 1.950,00), duas faturas próximas, pendências e acertos. */
export function agora(parcial: Partial<NowView> = {}): NowView {
  return {
    today: '2026-03-20',
    wallets: [
      { id: 'w-azul', name: 'Azul', color: '#1E88E5', currentBalanceCents: 180000 },
      { id: 'w-verde', name: 'Verde', color: '#43A047', currentBalanceCents: 15000 },
    ],
    archivedWalletsBalanceCents: 0,
    totalBalanceCents: 195000,
    invoices: [
      {
        creditCardId: 'c-azul',
        creditCardName: 'Azul',
        dueMonth: '2026-03',
        dueDate: '2026-03-25',
        status: 'closed',
        remainingCents: 20000,
      },
      {
        creditCardId: 'c-verde',
        creditCardName: 'Verde',
        dueMonth: '2026-04',
        dueDate: '2026-04-10',
        status: 'open',
        remainingCents: 30000,
      },
    ],
    pending: { count: 2, toReceiveCents: 10000, toPayCents: 4550 },
    settlements: [
      { tagId: 't-bruna', tagName: 'Bruna', accumulatedCents: 17000 },
      { tagId: 't-carla', tagName: 'Carla', accumulatedCents: -2000 },
    ],
    ...parcial,
  };
}
