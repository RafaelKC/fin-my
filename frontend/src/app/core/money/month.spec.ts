import {
  isYearMonth,
  monthLabel,
  monthOfDate,
  monthShortLabel,
  nextMonth,
  previousMonth,
} from './month';

describe('month', () => {
  it('formata o mês por extenso e abreviado', () => {
    expect(monthLabel('2026-03')).toBe('Março de 2026');
    expect(monthLabel('2026-12')).toBe('Dezembro de 2026');
    expect(monthShortLabel('2026-03')).toBe('MAR/26');
    expect(monthShortLabel('2005-09')).toBe('SET/05');
  });

  it('calcula mês anterior e seguinte, virando o ano', () => {
    expect(previousMonth('2026-03')).toBe('2026-02');
    expect(previousMonth('2026-01')).toBe('2025-12');
    expect(nextMonth('2026-03')).toBe('2026-04');
    expect(nextMonth('2025-12')).toBe('2026-01');
  });

  it('extrai o mês de uma data', () => {
    expect(monthOfDate('2026-03-15')).toBe('2026-03');
    expect(monthOfDate('2026-12-31')).toBe('2026-12');
  });

  it('valida o formato', () => {
    expect(isYearMonth('2026-03')).toBe(true);
    expect(isYearMonth('2026-13')).toBe(false);
    expect(isYearMonth('2026-3')).toBe(false);
    expect(() => monthLabel('2026-13')).toThrow(RangeError);
    expect(() => monthOfDate('2026-02-32')).toThrow(RangeError);
  });
});
