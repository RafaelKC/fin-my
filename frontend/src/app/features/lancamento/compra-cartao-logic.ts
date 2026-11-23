import {
  InstallmentMode,
  ValueParseResult,
  YearMonth,
  dueDate,
  formatBrl,
  generateInstallments,
  invoiceFor,
  monthLabel,
  parseValueExpression,
  ValueExpressionErrors,
} from '../../core/money';

export const PARCELAS_MAX = 48;

/** Valor da aba Cartão: aceita negativo (estorno, R6), recusa zero. */
export function parseValorCartao(text: string): ValueParseResult {
  const resultado = parseValueExpression(text, true);
  if (resultado.ok && resultado.cents === 0) {
    return { ok: false, error: ValueExpressionErrors.mustBePositive };
  }
  return resultado;
}

export interface EntradaPrevia {
  /** Data da compra (original, no parcelamento em andamento). */
  date: string;
  closingDay: number;
  dueDay: number;
  /** Valor digitado em centavos (pode ser negativo); `null` se inválido. */
  cents: number | null;
  /** N: total de parcelas (1 = à vista). */
  parcelas: number;
  modo: Exclude<InstallmentMode, 'in_progress'>;
  emAndamento: boolean;
  /** p: próxima parcela (só em andamento). */
  proxima: number;
  /** Fatura da parcela p (só em andamento). */
  faturaEmAndamento: YearMonth;
}

/** `2026-03` → `março`; com o ano quando difere de `ano`. */
function nomeMes(mes: YearMonth, ano: string): string {
  const nome = monthLabel(mes).split(' ')[0].toLowerCase();
  return mes.slice(0, 4) === ano ? nome : `${nome} de ${mes.slice(0, 4)}`;
}

/** `2026-03-25` → `25/03` (com o ano quando difere de `ano`). */
function vencimento(data: string, ano: string): string {
  const dia = `${data.slice(8, 10)}/${data.slice(5, 7)}`;
  return data.slice(0, 4) === ano ? dia : `${dia}/${data.slice(0, 4)}`;
}

/**
 * Linha de pré-visualização da aba Cartão (FR-010, US2.1, US3.3), pela porta TS de `InvoiceCycle` e
 * `InstallmentGenerator`: "Fatura de março — vence 25/03"; parcelado: "3x de R$ 33,34 (1ª) e
 * R$ 33,33 — primeira fatura: fevereiro". `null` enquanto faltar dado válido.
 */
export function previaCompra(e: EntradaPrevia): string | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(e.date)) return null;
  const ano = e.date.slice(0, 4);
  const primeira = e.emAndamento ? e.faturaEmAndamento : invoiceFor(e.date, e.closingDay, e.dueDay);

  if (!e.emAndamento && e.parcelas <= 1) {
    return `Fatura de ${nomeMes(primeira, ano)} — vence ${vencimento(dueDate(primeira, e.dueDay), ano)}`;
  }

  if (e.cents === null || e.cents <= 0) return null;
  const sufixo = `primeira fatura: ${nomeMes(primeira, ano)}`;

  if (e.emAndamento) {
    if (e.proxima < 1 || e.proxima > e.parcelas) return null;
    const restantes = e.parcelas - e.proxima + 1;
    const quais =
      restantes === 1
        ? `${e.parcelas}/${e.parcelas}`
        : `${e.proxima}/${e.parcelas} a ${e.parcelas}/${e.parcelas}`;
    const rotulo = restantes === 1 ? '1 parcela restante' : `${restantes} parcelas restantes`;
    return `${rotulo} (${quais}) de ${formatBrl(e.cents)} — ${sufixo}`;
  }

  // Total menor que N centavos daria parcelas zeradas: sem pré-visualização.
  if (e.modo === 'total_amount' && e.cents < e.parcelas) return null;
  const parcelas = generateInstallments(e.modo, e.parcelas, 1, e.cents, primeira);
  const primeiraParcela = parcelas[0].amountCents;
  const demais = parcelas[1].amountCents;
  if (e.modo === 'installment_amount') {
    return `${e.parcelas}x de ${formatBrl(primeiraParcela)} (total ${formatBrl(primeiraParcela * e.parcelas)}) — ${sufixo}`;
  }
  return primeiraParcela === demais
    ? `${e.parcelas}x de ${formatBrl(primeiraParcela)} — ${sufixo}`
    : `${e.parcelas}x de ${formatBrl(primeiraParcela)} (1ª) e ${formatBrl(demais)} — ${sufixo}`;
}
