import { YearMonth, addMonths, monthOfDate } from './month';

/** Ciclo da fatura de cartão (research R1): espelha `InvoiceCycle` do backend (RT-03). */

function validateDay(day: number, name: string): void {
  if (!Number.isInteger(day) || day < 1 || day > 31) {
    throw new RangeError(`${name} deve ser um inteiro entre 1 e 31.`);
  }
}

function pad(value: number): string {
  return value.toString().padStart(2, '0');
}

/** Data `AAAA-MM-DD` do dia no mês; dia inexistente vira o último dia do mês. */
function clamp(month: YearMonth, day: number): string {
  const year = Number(month.slice(0, 4));
  const m = Number(month.slice(5, 7));
  const lastDay = new Date(Date.UTC(year, m, 0)).getUTCDate();
  return `${month}-${pad(Math.min(day, lastDay))}`;
}

/**
 * Data de fechamento da fatura que vence em `dueMonth`: se `closingDay` < `dueDay` fecha no próprio
 * mês; senão (inclusive igual) fecha no mês anterior.
 */
export function closingDate(dueMonth: YearMonth, closingDay: number, dueDay: number): string {
  validateDay(closingDay, 'closingDay');
  validateDay(dueDay, 'dueDay');
  return clamp(closingDay < dueDay ? dueMonth : addMonths(dueMonth, -1), closingDay);
}

/** Data de vencimento da fatura de `dueMonth`. */
export function dueDate(dueMonth: YearMonth, dueDay: number): string {
  validateDay(dueDay, 'dueDay');
  return clamp(dueMonth, dueDay);
}

/**
 * Fatura (mês de vencimento) que recebe uma compra de `date` (`AAAA-MM-DD`): a primeira cuja data de
 * fechamento é posterior à compra. Compra no dia do fechamento vai para a fatura seguinte.
 */
export function invoiceFor(date: string, closingDay: number, dueDay: number): YearMonth {
  let candidate = monthOfDate(date);
  for (let i = 0; i < 3; i++, candidate = addMonths(candidate, 1)) {
    // Datas ISO `AAAA-MM-DD` comparam corretamente como texto.
    if (closingDate(candidate, closingDay, dueDay) > date) {
      return candidate;
    }
  }

  throw new RangeError('Nenhuma fatura encontrada em três meses; dias do cartão inválidos.');
}
