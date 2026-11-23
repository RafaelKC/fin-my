import vectors from '../../../../../shared/test-vectors/invoice-cycle.json';
import { closingDate, dueDate, invoiceFor } from './invoice-cycle';
import { VectorFile, duplicateIds } from './vectors';

const file = vectors as VectorFile<
  { date: string; closingDay: number; dueDay: number },
  { invoiceMonth: string; closingDate: string; dueDate: string }
>;

describe('InvoiceCycle (vetores compartilhados)', () => {
  it('não tem id duplicado', () => {
    expect(file.cases.length).toBeGreaterThan(0);
    expect(duplicateIds(file)).toEqual([]);
  });

  for (const c of file.cases) {
    it(c.id, () => {
      const { date, closingDay, dueDay } = c.input;
      const month = invoiceFor(date, closingDay, dueDay);
      expect(month).toBe(c.expected.invoiceMonth);
      expect(closingDate(month, closingDay, dueDay)).toBe(c.expected.closingDate);
      expect(dueDate(month, dueDay)).toBe(c.expected.dueDate);
    });
  }
});

describe('InvoiceCycle', () => {
  it('rejeita dias fora de 1..31', () => {
    expect(() => closingDate('2026-03', 0, 10)).toThrow(RangeError);
    expect(() => dueDate('2026-03', 32)).toThrow(RangeError);
    expect(() => invoiceFor('2026-03-10', 1.5, 10)).toThrow(RangeError);
  });
});
