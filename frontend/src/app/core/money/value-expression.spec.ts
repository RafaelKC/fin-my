import vectors from '../../../../../shared/test-vectors/value-expression.json';
import { parseValueExpression } from './value-expression';
import { VectorFile, duplicateIds } from './vectors';

type Expected = { amountCents: number } | { error: string };
const file = vectors as VectorFile<{ text: string }, Expected>;

describe('parseValueExpression (vetores compartilhados)', () => {
  it('não tem id duplicado', () => {
    expect(file.cases.length).toBeGreaterThan(0);
    expect(duplicateIds(file)).toEqual([]);
  });

  for (const c of file.cases) {
    it(c.id, () => {
      const result = parseValueExpression(c.input.text);
      if ('error' in c.expected) {
        expect(result).toEqual({ ok: false, error: c.expected.error });
      } else {
        expect(result).toEqual({ ok: true, cents: c.expected.amountCents });
      }
    });
  }
});

describe('parseValueExpression', () => {
  it('aceita null e undefined como inválido', () => {
    expect(parseValueExpression(null)).toEqual({ ok: false, error: 'amount.invalid_expression' });
    expect(parseValueExpression(undefined)).toEqual({
      ok: false,
      error: 'amount.invalid_expression',
    });
  });

  it('rejeita texto com mais de 200 caracteres', () => {
    expect(parseValueExpression('1+'.repeat(100) + '1')).toEqual({
      ok: false,
      error: 'amount.invalid_expression',
    });
  });

  it('aceita 32 níveis de parênteses e rejeita 33', () => {
    const nested = (n: number) => '('.repeat(n) + '1' + ')'.repeat(n);
    expect(parseValueExpression(nested(32))).toEqual({ ok: true, cents: 100 });
    expect(parseValueExpression(nested(33))).toEqual({
      ok: false,
      error: 'amount.invalid_expression',
    });
  });

  it('erro de sintaxe tem prioridade sobre divisão por zero', () => {
    expect(parseValueExpression('1/0+')).toEqual({ ok: false, error: 'amount.invalid_expression' });
  });

  it('com allowNonPositive aceita zero e negativo, mas respeita o máximo', () => {
    expect(parseValueExpression('0', true)).toEqual({ ok: true, cents: 0 });
    expect(parseValueExpression('-5,50', true)).toEqual({ ok: true, cents: -550 });
    expect(parseValueExpression('-10000000', true)).toEqual({
      ok: false,
      error: 'amount.too_large',
    });
  });

  it('não sofre com ponto flutuante (1,005 → 101 centavos)', () => {
    expect(parseValueExpression('1,005')).toEqual({ ok: true, cents: 101 });
  });
});
