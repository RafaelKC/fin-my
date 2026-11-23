import vectors from '../../../../../shared/test-vectors/installments.json';
import {
  GeneratedInstallment,
  InstallmentMode,
  generateInstallments,
} from './installment-generator';
import { VectorFile, duplicateIds } from './vectors';

const file = vectors as VectorFile<
  {
    mode: InstallmentMode;
    installmentCount: number;
    firstNumber: number;
    amountCents: number;
    firstInvoiceMonth: string;
  },
  { installments: GeneratedInstallment[] }
>;

describe('generateInstallments (vetores compartilhados)', () => {
  it('não tem id duplicado', () => {
    expect(file.cases.length).toBeGreaterThan(0);
    expect(duplicateIds(file)).toEqual([]);
  });

  for (const c of file.cases) {
    it(c.id, () => {
      const i = c.input;
      expect(
        generateInstallments(
          i.mode,
          i.installmentCount,
          i.firstNumber,
          i.amountCents,
          i.firstInvoiceMonth,
        ),
      ).toEqual(c.expected.installments);
    });
  }
});

describe('generateInstallments', () => {
  it('rejeita entradas inválidas', () => {
    expect(() => generateInstallments('total_amount', 0, 1, 100, '2026-03')).toThrow(RangeError);
    expect(() => generateInstallments('total_amount', 3, 1, 0, '2026-03')).toThrow(RangeError);
    expect(() => generateInstallments('in_progress', 3, 4, 100, '2026-03')).toThrow(RangeError);
  });
});
