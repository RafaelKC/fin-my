import { today } from './today';

describe('today', () => {
  it('usa America/Sao_Paulo (UTC-3), não o fuso do aparelho', () => {
    // 02:30 UTC de 16/03 ainda é 23:30 de 15/03 em São Paulo.
    expect(today(new Date('2026-03-16T02:30:00Z'))).toBe('2026-03-15');
    expect(today(new Date('2026-03-16T03:00:00Z'))).toBe('2026-03-16');
  });

  it('devolve AAAA-MM-DD sem argumento', () => {
    expect(today()).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});
