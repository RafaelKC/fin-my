import { BrlPipe, formatBrl } from './brl';

describe('brl', () => {
  it.each([
    [0, 'R$ 0,00'],
    [5, 'R$ 0,05'],
    [100, 'R$ 1,00'],
    [10220, 'R$ 102,20'],
    [123456, 'R$ 1.234,56'],
    [999999999, 'R$ 9.999.999,99'],
    [-123456, '-R$ 1.234,56'],
    [-5, '-R$ 0,05'],
  ])('formata %i como %s', (cents, expected) => {
    expect(formatBrl(cents)).toBe(expected);
    expect(new BrlPipe().transform(cents)).toBe(expected);
  });

  it('o pipe devolve vazio sem valor', () => {
    expect(new BrlPipe().transform(null)).toBe('');
    expect(new BrlPipe().transform(undefined)).toBe('');
  });
});
