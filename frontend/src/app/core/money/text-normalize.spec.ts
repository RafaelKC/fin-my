import vectors from '../../../../../shared/test-vectors/text-normalize.json';
import { normalizeText } from './text-normalize';
import { VectorFile, duplicateIds } from './vectors';

const file = vectors as VectorFile<{ text: string }, { text: string }>;

describe('normalizeText (vetores compartilhados)', () => {
  it('não tem id duplicado', () => {
    expect(file.cases.length).toBeGreaterThan(0);
    expect(duplicateIds(file)).toEqual([]);
  });

  for (const c of file.cases) {
    it(c.id, () => {
      expect(normalizeText(c.input.text)).toBe(c.expected.text);
    });
  }
});
