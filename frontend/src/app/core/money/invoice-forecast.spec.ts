import vectors from '../../../../../shared/test-vectors/invoice-forecast.json';
import { estimateActive, plannedInvoice } from './invoice-forecast';
import { VectorFile, duplicateIds } from './vectors';

const file = vectors as VectorFile<
  {
    totalCents: number;
    paidCents: number;
    estimateCents: number | null;
    today: string;
    closingDate: string;
  },
  { plannedCents: number; estimateActive: boolean }
>;

describe('InvoiceForecast (vetores compartilhados)', () => {
  it('não tem id duplicado', () => {
    expect(file.cases.length).toBeGreaterThan(0);
    expect(duplicateIds(file)).toEqual([]);
  });

  for (const c of file.cases) {
    it(c.id, () => {
      const { totalCents, paidCents, estimateCents, today, closingDate } = c.input;
      expect(plannedInvoice(totalCents, paidCents, estimateCents, today, closingDate)).toBe(
        c.expected.plannedCents,
      );
      expect(estimateActive(estimateCents, today, closingDate)).toBe(c.expected.estimateActive);
    });
  }
});
