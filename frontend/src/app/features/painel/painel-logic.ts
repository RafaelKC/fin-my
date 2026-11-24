import { monthShortLabel } from '../../core/money';
import type { PontoAnual } from '../../shared/chart-anual/chart-anual-logic';
import type { InvoiceStatus } from '../cartoes/card.models';
import type { YearPanel } from './panel.models';

export type Leitura = 'planned' | 'actual';

/** Menor e maior ano aceitos pela API (RT-03). */
export const ANO_MINIMO = 2000;
export const ANO_MAXIMO = 2100;

export const ROTULO_SITUACAO_FATURA: Record<InvoiceStatus, string> = {
  no_amount_due: 'Sem valor a pagar',
  paid: 'Paga',
  open: 'Aberta',
  closed: 'Fechada',
  overdue: 'Vencida',
};

export interface LinhaPainel {
  month: string;
  rotulo: string;
  incomeCents: number;
  expenseCents: number;
  resultCents: number;
  finalBalanceCents: number;
}

/** `'2026'` → 2026; qualquer outra coisa (ou fora de 2000–2100) → `null`. */
export function lerAno(valor: string | null): number | null {
  if (valor === null || !/^\d{4}$/.test(valor)) return null;
  const ano = Number(valor);
  return ano >= ANO_MINIMO && ano <= ANO_MAXIMO ? ano : null;
}

/** As 12 linhas do painel na leitura escolhida (valores idênticos aos da visão do mês). */
export function linhasDoPainel(painel: YearPanel, leitura: Leitura): LinhaPainel[] {
  return painel.months.map((m) => {
    const c = m[leitura];
    return {
      month: m.month,
      rotulo: monthShortLabel(m.month).slice(0, 3),
      incomeCents: c.incomeCents,
      expenseCents: c.expenseCents,
      resultCents: c.resultCents,
      finalBalanceCents: c.finalBalanceCents,
    };
  });
}

export function pontosDoGrafico(linhas: readonly LinhaPainel[]): PontoAnual[] {
  return linhas.map((l) => ({
    rotulo: l.rotulo,
    entradasCents: l.incomeCents,
    saidasCents: l.expenseCents,
    saldoCents: l.finalBalanceCents,
  }));
}

/** Mês de hoje (`AAAA-MM`) se estiver no ano mostrado; senão `null` (nada a destacar). */
export function mesDeHojeNoAno(hoje: string, ano: number): string | null {
  return hoje.startsWith(`${ano}-`) ? hoje.slice(0, 7) : null;
}

/** Texto do acerto: quem deve a você ("Bruna te deve R$ 170,00") ou crédito da pessoa. */
export function ehCreditoDeAcerto(accumulatedCents: number): boolean {
  return accumulatedCents < 0;
}
