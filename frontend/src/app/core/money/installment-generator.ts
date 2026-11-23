import { splitMoney } from './money-split';
import { YearMonth, addMonths } from './month';

export type InstallmentMode = 'total_amount' | 'installment_amount' | 'in_progress';

export interface GeneratedInstallment {
  number: number;
  amountCents: number;
  dueMonth: YearMonth;
}

/**
 * Parcelas de uma compra parcelada (pré-visualização; espelha `InstallmentGenerator` do backend).
 * `installmentCount` = total N; `firstNumber` só vale em `in_progress` (gera p..N); `firstInvoiceMonth`
 * = fatura da primeira parcela gerada.
 */
export function generateInstallments(
  mode: InstallmentMode,
  installmentCount: number,
  firstNumber: number,
  amountCents: number,
  firstInvoiceMonth: YearMonth,
): GeneratedInstallment[] {
  if (!Number.isSafeInteger(installmentCount) || installmentCount < 1) {
    throw new RangeError('installmentCount deve ser um inteiro maior ou igual a 1.');
  }

  if (!Number.isSafeInteger(amountCents) || amountCents <= 0) {
    throw new RangeError('amountCents deve ser um inteiro positivo.');
  }

  let first = 1;
  if (mode === 'in_progress') {
    if (!Number.isSafeInteger(firstNumber) || firstNumber < 1 || firstNumber > installmentCount) {
      throw new RangeError('firstNumber deve estar entre 1 e installmentCount.');
    }
    first = firstNumber;
  }

  const count = installmentCount - first + 1;
  const amounts =
    mode === 'total_amount'
      ? splitMoney(amountCents, installmentCount)
      : new Array<number>(count).fill(amountCents);

  return amounts.map((amount, i) => ({
    number: first + i,
    amountCents: amount,
    dueMonth: addMonths(firstInvoiceMonth, i),
  }));
}
