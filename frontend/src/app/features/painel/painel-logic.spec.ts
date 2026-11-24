import { SummaryColumn } from '../mes/month.models';
import { YearPanel } from './panel.models';
import {
  ehCreditoDeAcerto,
  lerAno,
  linhasDoPainel,
  mesDeHojeNoAno,
  pontosDoGrafico,
} from './painel-logic';

const coluna = (income: number, expense: number, final: number): SummaryColumn => ({
  openingBalanceCents: 0,
  walletsInitialBalanceCents: 0,
  incomeCents: income,
  expenseCents: expense,
  resultCents: income - expense,
  transfersInCents: 0,
  transfersOutCents: 0,
  finalBalanceCents: final,
});

const painel: YearPanel = {
  year: 2026,
  today: '2026-03-20',
  months: [
    { month: '2026-01', planned: coluna(100, 40, 60), actual: coluna(90, 40, 50) },
    { month: '2026-02', planned: coluna(0, 80, -20), actual: coluna(0, 0, 50) },
  ],
  totals: {
    planned: { incomeCents: 100, expenseCents: 120, resultCents: -20, finalBalanceCents: -20 },
    actual: { incomeCents: 90, expenseCents: 40, resultCents: 50, finalBalanceCents: 50 },
  },
};

describe('painel-logic', () => {
  it('lerAno aceita 2000–2100 e rejeita o resto', () => {
    expect(lerAno('2026')).toBe(2026);
    expect(lerAno('1999')).toBeNull();
    expect(lerAno('2101')).toBeNull();
    expect(lerAno('abc')).toBeNull();
    expect(lerAno(null)).toBeNull();
  });

  it('linhasDoPainel usa a leitura escolhida', () => {
    expect(linhasDoPainel(painel, 'planned')[0]).toMatchObject({
      rotulo: 'JAN',
      incomeCents: 100,
      resultCents: 60,
      finalBalanceCents: 60,
    });
    expect(linhasDoPainel(painel, 'actual')[0].incomeCents).toBe(90);
  });

  it('pontosDoGrafico mapeia Entradas, Saídas e Saldo final', () => {
    expect(pontosDoGrafico(linhasDoPainel(painel, 'planned'))[1]).toEqual({
      rotulo: 'FEV',
      entradasCents: 0,
      saidasCents: 80,
      saldoCents: -20,
    });
  });

  it('mesDeHojeNoAno só destaca quando hoje está no ano mostrado', () => {
    expect(mesDeHojeNoAno('2026-03-20', 2026)).toBe('2026-03');
    expect(mesDeHojeNoAno('2026-03-20', 2025)).toBeNull();
  });

  it('saldo de acerto negativo é crédito da pessoa', () => {
    expect(ehCreditoDeAcerto(-2000)).toBe(true);
    expect(ehCreditoDeAcerto(17000)).toBe(false);
  });
});
