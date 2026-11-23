import vectors from '../../../../../shared/test-vectors/money-split.json';
import { splitMoney } from './money-split';
import { VectorFile, duplicateIds } from './vectors';

const file = vectors as VectorFile<{ totalCents: number; parts: number }, { partsCents: number[] }>;

describe('splitMoney (vetores compartilhados)', () => {
  it('não tem id duplicado', () => {
    expect(file.cases.length).toBeGreaterThan(0);
    expect(duplicateIds(file)).toEqual([]);
  });

  for (const c of file.cases) {
    it(c.id, () => {
      expect(splitMoney(c.input.totalCents, c.input.parts)).toEqual(c.expected.partsCents);
    });
  }
});

describe('splitMoney', () => {
  it('rejeita total negativo e partes menores que 1', () => {
    expect(() => splitMoney(-1, 2)).toThrow(RangeError);
    expect(() => splitMoney(100, 0)).toThrow(RangeError);
    expect(() => splitMoney(100, 1.5)).toThrow(RangeError);
  });
});
